using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Models;
using NAudio.Wave;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Intercepts typed text system-wide and fires multi-modal responses (audio, visual, haptic, XP)
    /// when configured keyword triggers are detected.
    /// Requires Patreon access.
    /// </summary>
    public class KeywordTriggerService : IDisposable
    {
        #region Fields

        private readonly StringBuilder _buffer = new(200);
        private DateTime _lastKeyTime = DateTime.MinValue;
        private DateTime _lastGlobalTriggerTime = DateTime.MinValue;
        private bool _isActive;
        private bool _disposed;

        // Own audio player to avoid conflicting with AudioService/SubliminalService
        private WaveOutEvent? _triggerPlayer;
        private AudioFileReader? _triggerAudioFile;
        private readonly object _audioLock = new();

        // Audio file search cache
        private string[]? _audioFilesCache;
        private DateTime _audioFilesCacheTime = DateTime.MinValue;
        private string[]? _modAudioFilesCache;
        private DateTime _modAudioFilesCacheTime = DateTime.MinValue;
        private string? _modAudioCacheModId;
        private readonly string _audioPath;

        // Session awareness callback
        private Func<bool>? _isSessionActive;

        /// <summary>
        /// True when the last OCR scan found keyword matches awaiting a quick confirmation scan.
        /// ScreenOcrService checks this after each scan to decide whether to re-scan immediately.
        /// </summary>
        public bool NeedsOcrConfirmation { get; private set; }

        /// <summary>Fired when a keyword trigger activates</summary>
        public event EventHandler<KeywordTrigger>? TriggerFired;

        // --- Awareness Engine: loop protection (temporal mute) ---
        // Maps lowercase keyword → UTC time when the mute expires. Muted keywords are
        // skipped across ALL sources (OCR / keyboard / clipboard) so a trigger's own
        // output (flash text, bubble, spoken word) cannot re-arm it.
        private readonly Dictionary<string, DateTime> _mutedKeywords = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _muteLock = new();

        // --- Awareness Engine: live pulse ring buffer ---
        // Bounded history of recent trigger fires for the Awareness tab pulse feed.
        private const int PulseBufferCapacity = 20;
        private readonly LinkedList<TriggerFireRecord> _recentFires = new();
        private readonly object _recentFiresLock = new();

        /// <summary>
        /// Snapshot of the most recent trigger fires, newest first. Used by the
        /// Awareness tab's "Last Detected" pulse feed.
        /// </summary>
        public IReadOnlyList<TriggerFireRecord> GetRecentFires()
        {
            lock (_recentFiresLock)
            {
                return _recentFires.ToArray();
            }
        }

        private bool IsKeywordMuted(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return false;
            // The _mutedKeywords dict is the single source of truth. RecordFire
            // writes to it only when AwarenessLoopProtectionEnabled is on, but
            // ForceMuteKeyword writes unconditionally for self-echoing effects,
            // so both cases are honored by a direct dict lookup here.
            lock (_muteLock)
            {
                if (_mutedKeywords.TryGetValue(keyword, out var expiresAt))
                {
                    if (DateTime.UtcNow < expiresAt) return true;
                    _mutedKeywords.Remove(keyword);
                }
                return false;
            }
        }

        /// <summary>
        /// Force a keyword into the mute set for the given duration, bypassing the
        /// <see cref="AppSettings.AwarenessLoopProtectionEnabled"/> gate that
        /// <see cref="RecordFire"/> checks. Used by self-echoing effects (currently
        /// only <see cref="KeywordVisualEffect.ExactSubliminal"/>) that render the
        /// keyword back on-screen and would otherwise re-trigger themselves via OCR.
        /// </summary>
        private void ForceMuteKeyword(string keyword, int muteMs)
        {
            if (string.IsNullOrEmpty(keyword) || muteMs <= 0) return;
            lock (_muteLock)
            {
                var expiresAt = DateTime.UtcNow.AddMilliseconds(muteMs);
                // Extend rather than shrink if an existing mute is already longer.
                if (!_mutedKeywords.TryGetValue(keyword, out var existing) || existing < expiresAt)
                    _mutedKeywords[keyword] = expiresAt;
            }
            App.Logger?.Debug("ForceMuteKeyword: '{Keyword}' muted for {Ms}ms (self-echo guard)", keyword, muteMs);
        }

        /// <summary>
        /// Snapshot the trigger's current action list as a set of display keys
        /// used by the "Last Detected" pulse feed to render chip icons. Captured
        /// at fire time so the feed still shows the right icons even after the
        /// trigger is later edited, uninstalled, or deleted.
        /// </summary>
        private static List<string> BuildActionKeySnapshot(KeywordTrigger trigger)
        {
            var keys = new List<string>();
            if (trigger.Actions == null) return keys;

            foreach (var action in trigger.Actions)
            {
                if (action == null || !action.Enabled) continue;
                switch (action)
                {
                    case PlayAudioAction:         keys.Add("PlayAudio"); break;
                    case HighlightAction:         keys.Add("Highlight"); break;
                    case HapticAction:            keys.Add("Haptic"); break;
                    // AddXpAction intentionally not snapshot'd — XP is an
                    // internal progression mechanic, not a user-visible effect,
                    // so it shouldn't appear as a chip in the Last Detected feed.
                    case AvatarCommentAction:     keys.Add("AvatarComment"); break;
                    case ExtendSessionAction ext: keys.Add($"ExtendSession:{ext.Minutes}"); break;
                    case ChasterAddTimeAction ch: keys.Add($"ChasterAddTime:{ch.Minutes}"); break;
                    case VisualEffectAction ve:   keys.Add($"VisualEffect:{ve.Effect}"); break;
                }
            }
            return keys;
        }

        private void RecordFire(KeywordTrigger trigger, string source)
        {
            var settings = App.Settings?.Current;

            // 1. Temporal mute — suppress this exact keyword for a short window across all sources.
            //    Two layers write to the same _mutedKeywords dict:
            //      a) Optional user-controlled loop protection window (AwarenessLoopProtectionMs)
            //      b) HARD per-keyword cooldown (KeywordPerKeywordCooldownSeconds, default 15s)
            //         which ALWAYS applies regardless of loop-protection setting.
            //    We take the longer of the two so a user who cranks loop protection above 15s
            //    still gets what they asked for.
            if (settings != null && !string.IsNullOrEmpty(trigger.Keyword))
            {
                int loopMs = settings.AwarenessLoopProtectionEnabled ? settings.AwarenessLoopProtectionMs : 0;
                int hardMs = settings.KeywordPerKeywordCooldownSeconds * 1000;
                int finalMs = Math.Max(loopMs, hardMs);

                if (finalMs > 0)
                {
                    var expiresAt = DateTime.UtcNow.AddMilliseconds(finalMs);
                    lock (_muteLock)
                    {
                        // Extend (don't shrink) existing mute entries — a ForceMuteKeyword
                        // already in progress should not be cut short by this.
                        if (!_mutedKeywords.TryGetValue(trigger.Keyword, out var existing) || existing < expiresAt)
                            _mutedKeywords[trigger.Keyword] = expiresAt;
                    }
                }
            }

            // 2. Live pulse ring buffer — newest entry at the front, drop oldest past capacity.
            var record = new TriggerFireRecord
            {
                Keyword = trigger.Keyword,
                TriggerId = trigger.Id,
                VisualEffect = trigger.VisualEffect,
                Source = source,
                FiredAt = DateTime.Now,
                ActionKeys = BuildActionKeySnapshot(trigger),
            };
            lock (_recentFiresLock)
            {
                _recentFires.AddFirst(record);
                while (_recentFires.Count > PulseBufferCapacity)
                    _recentFires.RemoveLast();
            }
        }

        #endregion

        #region Constructor

        public KeywordTriggerService()
        {
            _audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sub_audio");
            App.Logger?.Information("KeywordTriggerService initialized");
        }

        #endregion

        #region Public API

        /// <summary>
        /// Start listening for keyword triggers.
        /// </summary>
        public void Start()
        {
            if (_isActive) return;
            if (!HasAccess())
            {
                App.Logger?.Debug("KeywordTriggerService: No access (requires T2 or whitelist)");
                return;
            }

            _isActive = true;
            _buffer.Clear();
            App.Logger?.Information("KeywordTriggerService started");
        }

        /// <summary>
        /// Stop listening for keyword triggers.
        /// </summary>
        public void Stop()
        {
            if (!_isActive) return;
            _isActive = false;
            _buffer.Clear();
            StopTriggerAudio();
            App.Logger?.Information("KeywordTriggerService stopped");
        }

        /// <summary>
        /// Set a callback to check if a session engine is currently running.
        /// </summary>
        public void SetSessionActiveCallback(Func<bool> callback)
        {
            _isSessionActive = callback;
        }

        /// <summary>
        /// Called from the keyboard hook on every key press.
        /// Translates vkCode → character, appends to buffer, checks for matches.
        /// </summary>
        public void OnKeyPressed(Key key, int vkCode)
        {
            if (!_isActive || _disposed) return;

            var settings = App.Settings?.Current;
            if (settings == null || !settings.KeywordTriggersEnabled) return;

            // Check buffer timeout — reset if too much time has passed
            var now = DateTime.Now;
            var timeoutMs = settings.KeywordBufferTimeoutMs;
            if ((now - _lastKeyTime).TotalMilliseconds > timeoutMs && _buffer.Length > 0)
            {
                _buffer.Clear();
            }
            _lastKeyTime = now;

            // Handle special keys
            if (key == Key.Return || key == Key.Escape || key == Key.Tab)
            {
                _buffer.Clear();
                return;
            }

            if (key == Key.Back)
            {
                if (_buffer.Length > 0)
                    _buffer.Remove(_buffer.Length - 1, 1);
                return;
            }

            if (key == Key.Space)
            {
                _buffer.Append(' ');
                CheckForMatches();
                return;
            }

            // Translate vkCode → actual character using Win32
            var ch = TranslateVkCode(vkCode);
            if (ch == null) return;

            _buffer.Append(ch.Value);

            // Cap buffer length
            if (_buffer.Length > 200)
                _buffer.Remove(0, _buffer.Length - 200);

            CheckForMatches();
        }

        /// <summary>
        /// Check if user has access to keyword triggers (Patreon supporter).
        /// </summary>
        public static bool HasAccess()
        {
            var patreon = App.Patreon;
            if (patreon == null) return false;
            return patreon.HasPremiumAccess;
        }

        /// <summary>
        /// Find a matching audio file for a keyword in the sub_audio folder.
        /// Uses the same logic as SubliminalService.FindLinkedAudio.
        /// Checks active mod's flashes_audio/ directory first, then falls back to default sub_audio/.
        /// </summary>
        public string? FindLinkedAudio(string keyword)
        {
            var cleanText = keyword.Trim();
            var extensions = new[] { ".mp3", ".wav", ".ogg", ".MP3", ".WAV", ".OGG" };

            var textVariants = new[]
            {
                cleanText,
                cleanText.ToUpper(),
                cleanText.ToLower(),
                cleanText.Replace("\u2019", "'"),
                cleanText.Replace("'", "\u2019"),
                cleanText.ToUpper().Replace("\u2019", "'"),
            };

            // Check active mod's audio directory first
            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (modPath != null)
            {
                var modAudioDir = Path.Combine(modPath, "resources", "sounds", "flashes_audio");
                if (Directory.Exists(modAudioDir))
                {
                    var result = SearchAudioDirectory(modAudioDir, cleanText, textVariants, extensions, isModCache: true);
                    if (result != null) return result;
                }
            }

            // Fall back to default sub_audio directory
            return SearchAudioDirectory(_audioPath, cleanText, textVariants, extensions, isModCache: false);
        }

        private string? SearchAudioDirectory(string directory, string cleanText, string[] textVariants, string[] extensions, bool isModCache)
        {
            foreach (var textVar in textVariants)
            {
                foreach (var ext in extensions)
                {
                    var path = Path.Combine(directory, textVar + ext);
                    if (File.Exists(path)) return path;
                }
            }

            // Fallback: case-insensitive directory search (cached)
            try
            {
                if (Directory.Exists(directory))
                {
                    string[]? files;
                    if (isModCache)
                    {
                        var currentModId = App.Mods?.ActiveMod?.Id;
                        if (_modAudioFilesCache == null || _modAudioCacheModId != currentModId ||
                            (DateTime.UtcNow - _modAudioFilesCacheTime).TotalSeconds > 60)
                        {
                            _modAudioFilesCache = Directory.GetFiles(directory);
                            _modAudioFilesCacheTime = DateTime.UtcNow;
                            _modAudioCacheModId = currentModId;
                        }
                        files = _modAudioFilesCache;
                    }
                    else
                    {
                        if (_audioFilesCache == null || (DateTime.UtcNow - _audioFilesCacheTime).TotalSeconds > 60)
                        {
                            _audioFilesCache = Directory.GetFiles(directory);
                            _audioFilesCacheTime = DateTime.UtcNow;
                        }
                        files = _audioFilesCache;
                    }

                    var normalizedText = cleanText.ToUpperInvariant().Replace("\u2019", "'");
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file).ToUpperInvariant().Replace("\u2019", "'");
                        if (fileName == normalizedText)
                            return file;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("KeywordTriggerService: Error searching audio files in {Dir}: {Error}", directory, ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Import triggers from the existing CustomTriggers list, auto-matching audio files.
        /// </summary>
        public List<KeywordTrigger> ImportFromCustomTriggers()
        {
            var customTriggers = App.Settings?.Current?.CustomTriggers;
            if (customTriggers == null || customTriggers.Count == 0)
                return new List<KeywordTrigger>();

            var existing = App.Settings?.Current?.KeywordTriggers ?? new List<KeywordTrigger>();
            var existingKeywords = new HashSet<string>(
                existing.Select(t => t.Keyword.ToUpperInvariant()));

            var imported = new List<KeywordTrigger>();

            foreach (var trigger in customTriggers)
            {
                if (string.IsNullOrWhiteSpace(trigger)) continue;
                if (existingKeywords.Contains(trigger.ToUpperInvariant())) continue;

                var kt = new KeywordTrigger
                {
                    Keyword = trigger,
                    MatchType = KeywordMatchType.PlainText,
                    Enabled = true,
                    CooldownSeconds = 30,
                    AudioFilePath = FindLinkedAudio(trigger),
                    AudioVolume = 80,
                    VisualEffect = KeywordVisualEffect.SubliminalFlash,
                    HapticEnabled = true,
                    HapticIntensity = 0.5,
                    DuckAudio = true,
                    XPAward = 10
                };

                imported.Add(kt);
            }

            return imported;
        }

        /// <summary>
        /// Run a synthetic trigger through the normal fire path — pulse feed update,
        /// TriggerFired event, and full action dispatch (avatar comment, highlight,
        /// audio, etc.). Used by the Awareness tutorial's demo step so the user can
        /// witness a fire without typing or waiting for OCR. Does not touch
        /// AppSettings.KeywordTriggers and bypasses all cooldowns / loop-protection.
        /// </summary>
        public void FireDemoTrigger(string keyword, string source = "Tutorial")
        {
            if (string.IsNullOrWhiteSpace(keyword)) return;

            var demo = new KeywordTrigger
            {
                Id = "tutorial:demo",
                Keyword = keyword,
                MatchType = KeywordMatchType.PlainText,
                Enabled = true,
                VisualEffect = KeywordVisualEffect.HighlightOnly,
                Actions = new List<KeywordAction>
                {
                    new HighlightAction { Enabled = true },
                    new AvatarCommentAction
                    {
                        Enabled = true,
                        FallbackPhraseCategory = "PuppyPraise",
                        RequireAiAvailable = false
                    }
                }
            };

            try
            {
                RecordFire(demo, source);
                TriggerFired?.Invoke(this, demo);
                _ = DispatchResponseAsync(demo, null);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FireDemoTrigger failed: {Error}", ex.Message);
            }
        }

        #endregion

        /// <summary>
        /// Returns the user's keyword triggers ordered so that installed preset
        /// clones (id prefix <c>preset:</c>) are checked BEFORE custom triggers.
        /// This ensures an explicit preset install wins when it collides with a
        /// pre-existing user trigger on the same keyword (e.g. both the "Puppy Pet"
        /// preset and an old custom trigger both matching "good boy"). Without
        /// this ordering, whichever trigger was saved first in the list wins,
        /// which is typically the stale custom one.
        /// </summary>
        private static IEnumerable<KeywordTrigger> OrderedByPresetPriority(List<KeywordTrigger> triggers)
        {
            // Materialize once; small list, cheap to partition.
            for (int i = 0; i < triggers.Count; i++)
            {
                var t = triggers[i];
                if (t?.Id?.StartsWith("preset:", StringComparison.Ordinal) == true)
                    yield return t;
            }
            for (int i = 0; i < triggers.Count; i++)
            {
                var t = triggers[i];
                if (t?.Id?.StartsWith("preset:", StringComparison.Ordinal) != true)
                    yield return t;
            }
        }

        #region OCR Word Matching

        // Two layers of tracking:
        //   _pendingOcrPositions   — position keys seen last scan, for two-scan stability check (anti-scroll)
        //   _highlightedOcrKeys    — position keys (text + bucketed x,y) already highlighted.
        //                            A position-keyed entry is removed the moment that specific
        //                            instance is no longer visible (scrolled off, covered by another
        //                            window, window closed, etc.). This gives the user's desired
        //                            behavior: each word instance fires exactly ONCE while it stays
        //                            on screen, and is eligible to fire again if it reappears.
        private HashSet<string> _pendingOcrPositions = new();
        private readonly HashSet<string> _highlightedOcrKeys = new();

        /// <summary>
        /// Process all OCR word hits from a single scan. Matches trigger keywords,
        /// confirms positions are stable across two scans (scroll-proof), and only
        /// highlights keywords whose text hasn't been highlighted yet.
        /// </summary>
        public void CheckOcrWords(List<OcrWordHit> allWords)
        {
            NeedsOcrConfirmation = false;

            if (!_isActive || _disposed) return;
            if (allWords == null || allWords.Count == 0)
            {
                _pendingOcrPositions.Clear();
                _highlightedOcrKeys.Clear();
                return;
            }

            // Diagnostic: log the raw OCR tokens per scan so we can see exactly what
            // Windows OCR returned (tokenization + casing). Helps debug issues like
            // "GOOD BOY doesn't match" where OCR may merge all-caps into one token.
            // Keep this short — log at most the first 40 tokens and truncate long ones.
            if (App.Logger != null)
            {
                var snippet = new System.Text.StringBuilder();
                int n = Math.Min(40, allWords.Count);
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) snippet.Append(' ');
                    var t = allWords[i].Text ?? "";
                    if (t.Length > 20) t = t.Substring(0, 20) + "…";
                    snippet.Append('"').Append(t).Append('"');
                }
                if (allWords.Count > n) snippet.Append(" …+").Append(allWords.Count - n);
                App.Logger.Information("OCR tokens ({Total}): {Tokens}", allWords.Count, snippet.ToString());
            }

            var settings = App.Settings?.Current;
            if (settings == null || !settings.KeywordTriggersEnabled) return;

            var triggers = settings.KeywordTriggers;
            if (triggers == null || triggers.Count == 0) return;

            // Global cooldown: enforce a hard minimum gap between ANY trigger fire
            // regardless of source (OCR, keyboard, text). Without this, OCR alone
            // can spam-fire every scan interval (3s default) as the avatar reply
            // bubble gets re-read on the next scan — a feedback loop. Guarding
            // here is the same pattern as CheckForMatches / CheckTextForMatches.
            if ((DateTime.Now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds)
                return;

            // 1. Find all words matching any enabled trigger.
            //    Iterate preset clones before custom triggers so an installed preset
            //    wins when the user also has a custom trigger on the same keyword.
            //    Collect ALL matching triggers (not just the first) so their actions
            //    can be merged by DispatchMergedAsync. See Feature D in the plan.
            var matchedWords = new List<OcrWordHit>();
            var firedTriggers = new List<KeywordTrigger>();

            foreach (var trigger in OrderedByPresetPriority(triggers))
            {
                if (!trigger.Enabled || string.IsNullOrEmpty(trigger.Keyword)) continue;
                if (trigger.MatchType == KeywordMatchType.Regex) continue;
                if (IsKeywordMuted(trigger.Keyword)) continue; // per-keyword hard cooldown
                if (trigger.IsOnCooldown) continue;             // per-trigger cooldown

                var words = FindMatchedWords(trigger.Keyword, allWords);
                if (words != null && words.Count > 0)
                {
                    firedTriggers.Add(trigger);
                    matchedWords.AddRange(words);
                }
            }

            if (matchedWords.Count == 0 || firedTriggers.Count == 0)
            {
                _pendingOcrPositions.Clear();
                _highlightedOcrKeys.Clear();
                return;
            }

            // effectTrigger is now a helper alias for logging/recording the primary
            // trigger; the dispatch uses the full firedTriggers list.
            var effectTrigger = firedTriggers[0];

            // 2. Build position set + word lookup (deduplicated).
            //    The position bucket deliberately spans a large area (~120 px) so
            //    existing highlighted words don't get re-flagged as "new instance"
            //    when typing in an editor nudges the text by 20–40 pixels due to
            //    word wrap or scrollbar reflow. Users who actually add a NEW copy
            //    of a trigger word at a totally different spot (>120 px away) get
            //    a fresh highlight, which matches expectations.
            const int PositionBucket = 120;
            var currentPositions = new HashSet<string>();
            var wordsByKey = new Dictionary<string, OcrWordHit>();

            foreach (var word in matchedWords)
            {
                var key = $"{word.Text.ToLowerInvariant()}_{word.ScreenRect.X / PositionBucket}_{word.ScreenRect.Y / PositionBucket}";
                if (currentPositions.Add(key))
                    wordsByKey[key] = word;
            }

            // 3. Drop guard entries whose specific instance is no longer on screen.
            //    "Instance" here means the (text + bucketed x,y) position key, so a
            //    word that stays at the same place stays guarded, but one that scrolls
            //    off (or gets covered) is forgotten and will re-fire if it reappears.
            _highlightedOcrKeys.IntersectWith(currentPositions);

            var newWords = new List<OcrWordHit>();
            var newKeys = new HashSet<string>();

            bool highlightAll = settings.OcrHighlightAll;

            if (highlightAll)
            {
                // "All matches" mode: skip two-scan stability gate, highlight every
                // new instance immediately. An instance is "new" if its position key
                // isn't already in the guard set.
                foreach (var kvp in wordsByKey)
                {
                    if (!_highlightedOcrKeys.Contains(kvp.Key))
                    {
                        newWords.Add(kvp.Value);
                        newKeys.Add(kvp.Key);
                    }
                }
            }
            else
            {
                // "Random subset" mode: pick a random subset of unguarded matches
                var candidates = new List<KeyValuePair<string, OcrWordHit>>();
                foreach (var kvp in wordsByKey)
                {
                    if (!_highlightedOcrKeys.Contains(kvp.Key))
                        candidates.Add(kvp);
                }

                if (candidates.Count > 0)
                {
                    int count = Random.Shared.Next(1, candidates.Count + 1);
                    // Shuffle and take 'count' items
                    for (int i = candidates.Count - 1; i > 0; i--)
                    {
                        int j = Random.Shared.Next(i + 1);
                        (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                    }
                    for (int i = 0; i < count; i++)
                    {
                        newWords.Add(candidates[i].Value);
                        newKeys.Add(candidates[i].Key);
                    }
                }
            }

            // 6. Update tracking — mark each newly-fired position key as guarded
            //    so the same instance won't re-fire on subsequent scans while it
            //    remains at the same place.
            _pendingOcrPositions = currentPositions;
            _highlightedOcrKeys.UnionWith(newKeys);

            // Diagnostic: log how many position keys were guarded vs newly fired.
            // Helps debug "why are words re-highlighting" — a healthy stream of
            // the same text on screen should show guarded > 0 and new = 0.
            if (App.Logger != null && currentPositions.Count > 0)
            {
                App.Logger.Information(
                    "OCR guard: scan matched {Matched} positions, {New} new, {Guarded} already guarded",
                    currentPositions.Count, newKeys.Count, currentPositions.Count - newKeys.Count);
            }

            if (newWords.Count == 0 || effectTrigger == null) return;

            App.Logger?.Information("OCR keyword confirmed: [{Keywords}] — {Count} new words across {Triggers} trigger(s)",
                string.Join(",", firedTriggers.Select(t => t.Keyword)), newWords.Count, firedTriggers.Count);

            // 7. Record each fired trigger (each gets its own pulse-feed entry and
            //    its own per-keyword hard cooldown stamp) then dispatch merged once.
            var now = DateTime.Now;
            foreach (var t in firedTriggers)
            {
                t.LastTriggeredAt = now;
                RecordFire(t, "OCR");
                TriggerFired?.Invoke(this, t);
            }
            _lastGlobalTriggerTime = now;
            _ = DispatchMergedAsync(firedTriggers, newWords);
        }

        /// <summary>
        /// Check externally-provided text (e.g. from clipboard, other sources) for keyword matches.
        /// No word-position data — uses simple text matching with cooldowns.
        /// </summary>
        public void CheckTextForMatches(string text)
        {
            if (!_isActive || _disposed) return;
            if (string.IsNullOrEmpty(text)) return;

            var settings = App.Settings?.Current;
            if (settings == null || !settings.KeywordTriggersEnabled) return;

            var triggers = settings.KeywordTriggers;
            if (triggers == null || triggers.Count == 0) return;

            var now = DateTime.Now;
            if ((now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds)
                return;

            // Collect all triggers whose keyword appears in the text, then merge.
            var firedTriggers = new List<KeywordTrigger>();
            foreach (var trigger in OrderedByPresetPriority(triggers))
            {
                if (!trigger.Enabled || string.IsNullOrEmpty(trigger.Keyword)) continue;
                if (trigger.IsOnCooldown) continue;
                if (IsKeywordMuted(trigger.Keyword)) continue;

                bool matched = trigger.MatchType == KeywordMatchType.Regex
                    ? TryRegexMatch(text, trigger.Keyword)
                    : ContainsWholeWord(text, trigger.Keyword);

                if (matched)
                    firedTriggers.Add(trigger);
            }

            if (firedTriggers.Count == 0) return;

            foreach (var t in firedTriggers)
            {
                t.LastTriggeredAt = now;
                App.Logger?.Information("Keyword trigger fired (text): '{Keyword}' id={Id}", t.Keyword, t.Id);
                RecordFire(t, "Text");
                TriggerFired?.Invoke(this, t);
            }
            _lastGlobalTriggerTime = now;
            _ = DispatchMergedAsync(firedTriggers, null);
        }

        /// <summary>
        /// Find OCR words that correspond to a matched keyword.
        ///
        /// <para>Single-word keywords use a <b>whole-word</b> comparison: the OCR
        /// token's surrounding punctuation is stripped and then matched case-
        /// insensitively against the keyword. This prevents "sit" from firing on
        /// "intensity" / "possession" / "sitting" etc. Users who want substring
        /// behavior should flip the trigger to Regex mode and use their own pattern.</para>
        ///
        /// <para>Multi-word keywords use consecutive-word matching (each component
        /// whole-word-matched in order), which already avoided the substring problem.</para>
        /// </summary>
        private static List<OcrWordHit>? FindMatchedWords(string keyword, List<OcrWordHit> wordHits)
        {
            if (wordHits.Count == 0) return null;

            var keywordParts = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (keywordParts.Length == 1)
            {
                var target = keywordParts[0];
                var matches = wordHits.FindAll(w => IsWholeWordMatch(w.Text, target));
                return matches.Count > 0 ? matches : null;
            }

            // Multi-word — find ALL consecutive word sequences.
            // Components are compared whole-word (punctuation stripped) too.
            // For each matching sequence we emit a SINGLE merged hit whose
            // ScreenRect is the union of the participating words' rects, so
            // the highlight overlay draws one rectangle around the full phrase
            // rather than one per word.
            var results = new List<OcrWordHit>();
            for (int i = 0; i <= wordHits.Count - keywordParts.Length; i++)
            {
                bool sequenceMatch = true;
                for (int j = 0; j < keywordParts.Length; j++)
                {
                    if (!IsWholeWordMatch(wordHits[i + j].Text, keywordParts[j]))
                    {
                        sequenceMatch = false;
                        break;
                    }
                }

                if (!sequenceMatch) continue;

                var first = wordHits[i];
                int minX = first.ScreenRect.X;
                int minY = first.ScreenRect.Y;
                int maxRight = first.ScreenRect.Right;
                int maxBottom = first.ScreenRect.Bottom;
                for (int j = 1; j < keywordParts.Length; j++)
                {
                    var r = wordHits[i + j].ScreenRect;
                    if (r.X < minX) minX = r.X;
                    if (r.Y < minY) minY = r.Y;
                    if (r.Right > maxRight) maxRight = r.Right;
                    if (r.Bottom > maxBottom) maxBottom = r.Bottom;
                }

                results.Add(new OcrWordHit
                {
                    Text = keyword,
                    ScreenRect = new System.Drawing.Rectangle(
                        minX, minY, maxRight - minX, maxBottom - minY),
                    Screen = first.Screen,
                });
            }

            return results.Count > 0 ? results : null;
        }

        /// <summary>
        /// Case-insensitive whole-word comparison of an OCR token against a single
        /// keyword word. Leading/trailing punctuation on the OCR token is stripped
        /// before comparing so "sit." / "sit," / "'sit'" all match the keyword "sit"
        /// but "sitting" / "intensity" do not.
        /// </summary>
        private static bool IsWholeWordMatch(string ocrToken, string keywordWord)
        {
            if (string.IsNullOrEmpty(ocrToken) || string.IsNullOrEmpty(keywordWord)) return false;
            var stripped = StripEdgePunctuation(ocrToken);
            return stripped.Equals(keywordWord, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Strips leading/trailing non-letter-or-digit characters from a token.</summary>
        private static string StripEdgePunctuation(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int start = 0, end = s.Length - 1;
            while (start <= end && !char.IsLetterOrDigit(s[start])) start++;
            while (end >= start && !char.IsLetterOrDigit(s[end])) end--;
            return start > end ? string.Empty : s.Substring(start, end - start + 1);
        }

        /// <summary>
        /// Whole-word case-insensitive search inside a free-form text buffer
        /// (keyboard rolling buffer, clipboard text, etc.). Uses a regex with
        /// <c>\b</c> word boundaries around an escaped literal so "sit" matches
        /// "sit down" but not "intensity". Multi-word keywords are joined with
        /// <c>\s+</c> so they survive variable whitespace between words.
        /// </summary>
        private static bool ContainsWholeWord(string haystack, string keyword)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(keyword)) return false;
            try
            {
                var parts = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var escaped = string.Join("\\s+", parts.Select(Regex.Escape));
                var pattern = $@"\b{escaped}\b";
                return Regex.IsMatch(haystack, pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100));
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Matching

        private void CheckForMatches()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var triggers = settings.KeywordTriggers;
            if (triggers == null || triggers.Count == 0) return;

            // Check global cooldown
            var now = DateTime.Now;
            if ((now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds)
                return;

            var bufferText = _buffer.ToString();
            if (string.IsNullOrEmpty(bufferText)) return;

            // Collect all triggers whose keyword is in the rolling buffer, then
            // merge-dispatch. Clearing the buffer happens after the scan so that
            // all simultaneous hits in the same buffer are found in one pass.
            var firedTriggers = new List<KeywordTrigger>();
            foreach (var trigger in OrderedByPresetPriority(triggers))
            {
                if (!trigger.Enabled || string.IsNullOrEmpty(trigger.Keyword)) continue;
                if (trigger.IsOnCooldown) continue;
                if (IsKeywordMuted(trigger.Keyword)) continue;

                bool matched = trigger.MatchType == KeywordMatchType.Regex
                    ? TryRegexMatch(bufferText, trigger.Keyword)
                    : ContainsWholeWord(bufferText, trigger.Keyword);

                if (matched)
                    firedTriggers.Add(trigger);
            }

            if (firedTriggers.Count == 0) return;

            _buffer.Clear(); // Prevent re-triggering on the same buffer
            foreach (var t in firedTriggers)
            {
                t.LastTriggeredAt = now;
                App.Logger?.Information("Keyword trigger fired: '{Keyword}' id={Id}", t.Keyword, t.Id);
                RecordFire(t, "Keyboard");
                TriggerFired?.Invoke(this, t);
            }
            _lastGlobalTriggerTime = now;
            _ = DispatchMergedAsync(firedTriggers, null);
        }

        private static bool TryRegexMatch(string input, string pattern)
        {
            try
            {
                return Regex.IsMatch(input, pattern,
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100));
            }
            catch
            {
                // Bad regex pattern — silently fail
                return false;
            }
        }

        #endregion

        #region Response Dispatch

        /// <summary>
        /// Dispatches a trigger's response by iterating its composable Actions list.
        /// Falls back to the legacy flat-field path for any trigger whose Actions list
        /// is empty (defensive: post-migration this shouldn't happen).
        /// </summary>
        private async Task DispatchResponseAsync(KeywordTrigger trigger, List<OcrWordHit>? matchedWords = null)
        {
            try
            {
                if (_disposed) return;

                if (trigger.Actions != null && trigger.Actions.Count > 0)
                {
                    await DispatchActionsAsync(trigger, matchedWords);
                    return;
                }

                // Defensive fallback: migration should populate Actions, but if a trigger
                // arrived here without one, run the legacy flat-field dispatch so we don't
                // silently swallow its effect.
                await DispatchLegacyFlatFieldsAsync(trigger, matchedWords);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("KeywordTriggerService: Error dispatching response: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Merge-dispatch for the case where multiple triggers matched the same
        /// scan (e.g. the user has "good boy" and "test" both visible on screen
        /// simultaneously). Walks the triggers in priority order, collects their
        /// Actions into a single list de-duplicated by <see cref="GetActionDedupKey"/>,
        /// and runs the merged list exactly once with the union of matched words.
        ///
        /// Dedup semantics (matches user's stated model):
        ///   - Highlight fires once with ALL matched words union'd
        ///   - PlayAudio fires once (first trigger that has one wins)
        ///   - AvatarComment uses first trigger's prompt + keyword
        ///   - Different VisualEffects (ImageFlash + OverlayPulse) both fire;
        ///     same VisualEffect from multiple triggers fires only once.
        /// </summary>
        private async Task DispatchMergedAsync(List<KeywordTrigger> firedTriggers, List<OcrWordHit>? matchedWords)
        {
            if (firedTriggers == null || firedTriggers.Count == 0) return;
            var first = firedTriggers[0];

            // If only one trigger matched, skip the merge allocation and dispatch directly.
            if (firedTriggers.Count == 1)
            {
                await DispatchResponseAsync(first, matchedWords);
                return;
            }

            var merged = new List<KeywordAction>();
            var seen = new HashSet<string>();
            foreach (var t in firedTriggers)
            {
                if (t.Actions == null) continue;
                foreach (var a in t.Actions)
                {
                    if (a == null || !a.Enabled) continue;
                    var key = GetActionDedupKey(a);
                    if (seen.Add(key))
                        merged.Add(a);
                }
            }

            App.Logger?.Information(
                "DispatchMerged: {N} trigger(s) [{Keywords}] → {Count} unique actions",
                firedTriggers.Count,
                string.Join(",", firedTriggers.Select(t => t.Keyword)),
                merged.Count);

            // Build a synthetic transient trigger so DispatchActionsAsync can reuse
            // its ducking/iteration logic unchanged. Borrows first trigger's Keyword
            // and Id for avatar-comment prompts and log context.
            var synthetic = new KeywordTrigger
            {
                Id = first.Id,
                Keyword = first.Keyword,
                VisualEffect = first.VisualEffect,
                Actions = merged,
            };
            await DispatchActionsAsync(synthetic, matchedWords);
        }

        /// <summary>
        /// Returns the dedup key used by <see cref="DispatchMergedAsync"/> to
        /// decide which actions survive when multiple triggers fire in the same
        /// scan. Different VisualEffect variants are treated as distinct, but
        /// every other action type has a single slot.
        /// </summary>
        private static string GetActionDedupKey(KeywordAction a) => a switch
        {
            PlayAudioAction        => "PlayAudio",
            HighlightAction        => "Highlight",
            HapticAction           => "Haptic",
            AddXpAction            => "AddXp",
            AvatarCommentAction    => "AvatarComment",
            VisualEffectAction ve  => $"VisualEffect:{ve.Effect}",
            ExtendSessionAction    => "ExtendSession",
            ChasterAddTimeAction   => "ChasterAddTime",
            _                      => a.GetType().Name,
        };

        /// <summary>
        /// Iterates the action list. Ducking is applied once if any PlayAudioAction
        /// requests it, and unducked after the longest-running audio clip finishes.
        /// </summary>
        private async Task DispatchActionsAsync(KeywordTrigger trigger, List<OcrWordHit>? matchedWords)
        {
            long duckGen = -1;
            bool didDuck = false;
            double maxAudioDuration = 0;

            var actions = trigger.Actions.Where(a => a != null && a.Enabled).ToList();

            App.Logger?.Information("DispatchActions '{Kw}' id={Id} actions=[{List}]",
                trigger.Keyword, trigger.Id,
                string.Join(",", actions.Select(a => a.GetType().Name + (a is PlayAudioAction p ? $"({p.FilePath})" : ""))));

            // Duck once up-front if any audio action wants it.
            bool anyDuck = actions.OfType<PlayAudioAction>().Any(a => a.DuckSystemAudio);
            if (anyDuck && App.Settings?.Current?.AudioDuckingEnabled == true)
            {
                App.Audio?.Duck(App.Settings?.Current?.DuckingLevel ?? 80);
                duckGen = App.Audio?.DuckGeneration ?? -1;
                didDuck = true;
            }

            try
            {
                foreach (var action in actions)
                {
                    if (_disposed) break;
                    var dur = await DispatchActionAsync(action, trigger, matchedWords);
                    if (dur > maxAudioDuration) maxAudioDuration = dur;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error("KeywordTriggerService: Action dispatch error: {Error}", ex.Message);
            }

            if (!didDuck) return;

            if (_disposed)
            {
                App.Audio?.Unduck(duckGen);
                return;
            }

            if (maxAudioDuration > 0)
                await Task.Delay(TimeSpan.FromSeconds(maxAudioDuration + 0.5));
            else
                await Task.Delay(500);
            App.Audio?.Unduck(duckGen);
        }

        /// <summary>
        /// Runs a single action and returns any audio duration it kicked off (for unduck timing).
        /// </summary>
        private async Task<double> DispatchActionAsync(KeywordAction action, KeywordTrigger trigger, List<OcrWordHit>? matchedWords)
        {
            switch (action)
            {
                case PlayAudioAction audio:
                    return await DispatchPlayAudioAsync(audio);

                case VisualEffectAction visual:
                    Application.Current?.Dispatcher?.Invoke(() => FireVisualEffect(visual.Effect, trigger));
                    return 0;

                case HighlightAction:
                    {
                        var hasMatched = matchedWords != null && matchedWords.Count > 0;
                        var hlEnabled = App.Settings?.Current?.KeywordHighlightEnabled == true;
                        App.Logger?.Information("HighlightAction: matchedWords={Count} hlEnabled={Enabled}",
                            hasMatched ? matchedWords!.Count : 0, hlEnabled);
                        if (hasMatched && hlEnabled)
                        {
                            Application.Current?.Dispatcher?.Invoke(() =>
                                App.KeywordHighlight?.ShowHighlight(matchedWords));
                        }
                    }
                    return 0;

                case HapticAction haptic:
                    _ = App.Haptics?.TriggerSubliminalPatternAsync(trigger.Keyword);
                    return 0;

                case AddXpAction xp:
                    if (xp.Amount > 0)
                    {
                        var xpAmount = (double)xp.Amount;
                        if (_isSessionActive?.Invoke() == true)
                        {
                            var multiplier = App.Settings?.Current?.KeywordSessionMultiplier ?? 1.5;
                            xpAmount *= multiplier;
                        }
                        App.Progression?.AddXP(xpAmount, XPSource.KeywordTrigger, XPContext.FromCurrentSettings());
                    }
                    return 0;

                case AvatarCommentAction comment:
                    DispatchAvatarComment(comment, trigger);
                    return 0;

                case ExtendSessionAction ext:
                    App.Logger?.Information("KeywordTriggerService: ExtendSessionAction stubbed (+{Min}m) — session API pending", ext.Minutes);
                    return 0;

                case ChasterAddTimeAction chas:
                    App.Logger?.Information("KeywordTriggerService: ChasterAddTimeAction stubbed (+{Min}m) — Chaster integration pending", chas.Minutes);
                    return 0;

                default:
                    return 0;
            }
        }

        /// <summary>Plays a configured audio clip N times with a delay between plays. Returns the clip's duration.</summary>
        private async Task<double> DispatchPlayAudioAsync(PlayAudioAction audio)
        {
            if (string.IsNullOrEmpty(audio.FilePath))
            {
                App.Logger?.Information("PlayAudioAction: empty FilePath, skipping");
                return 0;
            }

            var resolved = ResolveAudioPath(audio.FilePath);
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
            {
                App.Logger?.Warning("PlayAudioAction: could not resolve '{Path}' → '{Resolved}'",
                    audio.FilePath, resolved);
                return 0;
            }

            App.Logger?.Information("PlayAudioAction: playing '{Path}' at vol {Vol}% x{Count}",
                resolved, audio.Volume, audio.PlayCount);

            double lastDuration = 0;
            var count = Math.Max(1, audio.PlayCount);
            for (int i = 0; i < count; i++)
            {
                if (_disposed) break;
                if (i > 0 && audio.DelayBetweenMs > 0)
                    await Task.Delay(audio.DelayBetweenMs);
                lastDuration = PlayTriggerAudio(resolved, audio.Volume);
            }

            App.Logger?.Information("PlayAudioAction: returned duration {Dur:0.00}s", lastDuration);
            return lastDuration;
        }

        /// <summary>
        /// Allows preset JSONs to ship relative paths like "AwarenessPresets/audio/clicker.mp3"
        /// by looking them up under the app's Resources folder. Absolute paths pass through.
        /// </summary>
        private static string ResolveAudioPath(string path)
        {
            if (Path.IsPathRooted(path)) return path;

            // Look under Resources/ first (preset-bundled audio).
            var resPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", path);
            if (File.Exists(resPath)) return resPath;

            // Fall back to sub_audio directory (legacy relative audio).
            var subPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sub_audio", path);
            if (File.Exists(subPath)) return subPath;

            return path;
        }

        /// <summary>
        /// Fire-and-forget avatar comment. Uses AI if available, falls back to canned
        /// phrase pool. Never blocks the dispatcher loop.
        /// </summary>
        private void DispatchAvatarComment(AvatarCommentAction a, KeywordTrigger trigger)
        {
            var aiAvailable = App.Ai?.IsAvailable == true;

            if (a.RequireAiAvailable && !aiAvailable)
            {
                var canned = PickCannedPhrase(a.FallbackPhraseCategory);
                if (!string.IsNullOrEmpty(canned))
                    ShowAvatarLine(canned);
                return;
            }

            var keyword = trigger.Keyword;
            var promptTemplate = a.PromptTemplate;
            var fallbackCategory = a.FallbackPhraseCategory;

            _ = Task.Run(async () =>
            {
                try
                {
                    string? line = null;
                    if (aiAvailable && App.Ai != null)
                        line = await App.Ai.GetKeywordCommentAsync(keyword, promptTemplate);

                    if (string.IsNullOrEmpty(line))
                        line = PickCannedPhrase(fallbackCategory);

                    if (!string.IsNullOrEmpty(line))
                        ShowAvatarLine(line);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("KeywordTriggerService: AvatarComment dispatch failed: {Error}", ex.Message);
                }
            });
        }

        private static string? PickCannedPhrase(string? category)
        {
            if (string.IsNullOrEmpty(category)) return null;
            try
            {
                var phrases = App.CompanionPhrases?.GetEnabledPhrases(category);
                if (phrases == null || phrases.Length == 0)
                    phrases = App.Mods?.GetPhrases(category);
                if (phrases == null || phrases.Length == 0) return null;
                return phrases[Random.Shared.Next(phrases.Length)];
            }
            catch
            {
                return null;
            }
        }

        private static void ShowAvatarLine(string line)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { App.AvatarWindow?.GigglePriority(line, playSound: true); }
                catch (Exception ex) { App.Logger?.Debug("AvatarWindow.GigglePriority failed: {Error}", ex.Message); }
            }));
        }

        /// <summary>
        /// Legacy fallback: the original flat-field dispatch path, run only when a
        /// trigger somehow arrives with an empty Actions list.
        /// </summary>
        private async Task DispatchLegacyFlatFieldsAsync(KeywordTrigger trigger, List<OcrWordHit>? matchedWords)
        {
            long duckGen = -1;
            try
            {
                if (_disposed) return;

                if (trigger.VisualEffect == KeywordVisualEffect.HighlightOnly)
                {
                    if (matchedWords != null && matchedWords.Count > 0
                        && App.Settings?.Current?.KeywordHighlightEnabled == true)
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                            App.KeywordHighlight?.ShowHighlight(matchedWords));
                    }
                    return;
                }

                if (trigger.DuckAudio && App.Settings?.Current?.AudioDuckingEnabled == true)
                {
                    App.Audio?.Duck(App.Settings?.Current?.DuckingLevel ?? 80);
                    duckGen = App.Audio?.DuckGeneration ?? -1;
                }

                double audioDuration = 0;
                if (!string.IsNullOrEmpty(trigger.AudioFilePath) && File.Exists(trigger.AudioFilePath))
                {
                    for (int i = 0; i < trigger.AudioPlayCount; i++)
                    {
                        if (_disposed) break;
                        if (i > 0 && trigger.AudioDelayBetweenMs > 0)
                            await Task.Delay(trigger.AudioDelayBetweenMs);
                        audioDuration = PlayTriggerAudio(trigger.AudioFilePath, trigger.AudioVolume);
                    }
                }

                if (_disposed) return;

                Application.Current?.Dispatcher?.Invoke(() => FireVisualEffect(trigger.VisualEffect, trigger));

                if (matchedWords != null && matchedWords.Count > 0
                    && App.Settings?.Current?.KeywordHighlightEnabled == true)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                        App.KeywordHighlight?.ShowHighlight(matchedWords));
                }

                if (trigger.HapticEnabled)
                    _ = App.Haptics?.TriggerSubliminalPatternAsync(trigger.Keyword);

                if (trigger.XPAward > 0)
                {
                    var xpAmount = (double)trigger.XPAward;
                    if (_isSessionActive?.Invoke() == true)
                    {
                        var multiplier = App.Settings?.Current?.KeywordSessionMultiplier ?? 1.5;
                        xpAmount *= multiplier;
                    }
                    App.Progression?.AddXP(xpAmount, XPSource.KeywordTrigger, XPContext.FromCurrentSettings());
                }

                if (_disposed) return;
                if (trigger.DuckAudio && audioDuration > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(audioDuration + 0.5));
                    App.Audio?.Unduck(duckGen);
                }
                else if (trigger.DuckAudio)
                {
                    await Task.Delay(500);
                    App.Audio?.Unduck(duckGen);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error("KeywordTriggerService: Legacy dispatch error: {Error}", ex.Message);
                if (trigger.DuckAudio)
                    App.Audio?.Unduck(duckGen);
            }
        }

        /// <summary>
        /// Rebuild the <see cref="KeywordTrigger.Actions"/> list from the legacy flat
        /// fields. Used by migration on load and by the Exclusives editor on save so
        /// users editing the flat-field UI stay compatible with the dispatcher loop.
        /// </summary>
        public static void RebuildActionsFromFlatFields(KeywordTrigger trigger)
        {
            if (trigger == null) return;

            var list = new List<KeywordAction>();

            if (!string.IsNullOrEmpty(trigger.AudioFilePath))
            {
                list.Add(new PlayAudioAction
                {
                    FilePath = trigger.AudioFilePath,
                    Volume = trigger.AudioVolume,
                    PlayCount = trigger.AudioPlayCount,
                    DelayBetweenMs = trigger.AudioDelayBetweenMs,
                    DuckSystemAudio = trigger.DuckAudio,
                });
            }

            if (trigger.VisualEffect != KeywordVisualEffect.None &&
                trigger.VisualEffect != KeywordVisualEffect.HighlightOnly)
            {
                list.Add(new VisualEffectAction { Effect = trigger.VisualEffect });
            }

            // Always include Highlight — it self-guards on matchedWords != null & global setting.
            list.Add(new HighlightAction());

            if (trigger.HapticEnabled)
                list.Add(new HapticAction { Intensity = trigger.HapticIntensity });

            if (trigger.XPAward > 0)
                list.Add(new AddXpAction { Amount = trigger.XPAward });

            trigger.Actions = list;
        }

        private void FireVisualEffect(KeywordVisualEffect effect, KeywordTrigger trigger)
        {
            try
            {
                switch (effect)
                {
                    case KeywordVisualEffect.SubliminalFlash:
                        // Flash a subliminal from the user's configured pool
                        App.Subliminal?.FlashSubliminal();
                        break;

                    case KeywordVisualEffect.ExactSubliminal:
                        // Flash the matched keyword itself as subliminal text.
                        // CRITICAL: this effect echoes the keyword back on-screen, which
                        // OCR will happily re-read and re-fire. Always force-mute the
                        // keyword for a window longer than a full OCR scan, regardless
                        // of the global AwarenessLoopProtectionEnabled setting — the
                        // alternative is an infinite feedback loop.
                        App.Subliminal?.FlashSubliminalCustom(trigger.Keyword.ToUpperInvariant());
                        ForceMuteKeyword(trigger.Keyword, 3000);
                        break;

                    case KeywordVisualEffect.ImageFlash:
                        // Trigger a single image flash
                        App.Flash?.TriggerFlashOnce();
                        break;

                    case KeywordVisualEffect.OverlayPulse:
                        // Briefly double overlay intensity then restore
                        App.Overlay?.PulseOverlays();
                        break;

                    case KeywordVisualEffect.MindWipe:
                        // Only trigger if audio files exist (TriggerOnce shows a MessageBox when empty)
                        if (App.MindWipe?.AudioFileCount > 0)
                            App.MindWipe.TriggerOnce();
                        break;

                    case KeywordVisualEffect.Bubbles:
                        App.Bubbles?.SpawnOnce();
                        break;

                    case KeywordVisualEffect.HighlightOnly:
                    case KeywordVisualEffect.None:
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("KeywordTriggerService: Error firing visual effect: {Error}", ex.Message);
            }
        }

        #endregion

        #region Audio

        /// <summary>
        /// One-shot preview playback for a clip path at a given volume. Used by
        /// the Awareness preset detail dialog's Test button so users can audition
        /// a file they're about to assign to a trigger. Resolves relative preset
        /// paths the same way dispatch does.
        /// </summary>
        public double PreviewAudioClip(string path, int volumePercent)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            var resolved = ResolveAudioPath(path);
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved)) return 0;
            return PlayTriggerAudio(resolved, volumePercent);
        }

        private double PlayTriggerAudio(string path, int volumePercent)
        {
            lock (_audioLock)
            {
                try
                {
                    StopTriggerAudio();

                    if (!File.Exists(path))
                    {
                        App.Logger?.Warning("PlayTriggerAudio: file not found '{Path}'", path);
                        return 0;
                    }

                    _triggerAudioFile = new AudioFileReader(path);
                    _triggerPlayer = new WaveOutEvent();

                    // Apply volume curve (same as AudioService)
                    var volume = volumePercent / 100.0f;
                    var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100.0f;
                    var curvedVolume = Math.Max(0.05f, (float)Math.Pow(volume * masterVolume, 1.5));
                    _triggerAudioFile.Volume = curvedVolume;

                    _triggerPlayer.Init(_triggerAudioFile);
                    _triggerPlayer.Play();

                    App.Logger?.Information("PlayTriggerAudio: playing '{Path}' curved={Curve:0.00} master={Master:0.00} thread={Thread}",
                        Path.GetFileName(path), curvedVolume, masterVolume, System.Threading.Thread.CurrentThread.ManagedThreadId);

                    return _triggerAudioFile.TotalTime.TotalSeconds;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("PlayTriggerAudio: Error playing audio: {Error}", ex.Message);
                    return 0;
                }
            }
        }

        private void StopTriggerAudio()
        {
            lock (_audioLock)
            {
                try
                {
                    _triggerPlayer?.Stop();
                    _triggerPlayer?.Dispose();
                    _triggerAudioFile?.Dispose();
                }
                catch { }

                _triggerPlayer = null;
                _triggerAudioFile = null;
            }
        }

        #endregion

        #region Win32 Key Translation

        /// <summary>
        /// Translate a virtual key code to the actual typed character,
        /// accounting for Shift, CapsLock, and keyboard layout.
        /// </summary>
        private static char? TranslateVkCode(int vkCode)
        {
            try
            {
                var keyboardState = new byte[256];
                if (!GetKeyboardState(keyboardState))
                    return null;

                var scanCode = MapVirtualKey((uint)vkCode, 0);
                var chars = new StringBuilder(4);

                var result = ToUnicode(
                    (uint)vkCode, scanCode, keyboardState,
                    chars, chars.Capacity, 0);

                if (result == 1)
                    return chars[0];

                return null;
            }
            catch
            {
                return null;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern int ToUnicode(
            uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
            int cchBuff, uint wFlags);

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            StopTriggerAudio();
        }

        #endregion
    }
}
