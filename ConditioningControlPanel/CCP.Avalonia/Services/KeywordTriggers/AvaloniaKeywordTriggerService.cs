using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.AIService;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Companion;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Services.KeywordTriggers;

/// <summary>
/// Avalonia implementation of the Awareness Engine keyword-trigger runtime.
/// Mirrors the legacy WPF <c>KeywordTriggerService</c> but resolves all
/// dependencies through Core/Avalonia DI seams instead of static <c>App.*</c>.
/// </summary>
public sealed class AvaloniaKeywordTriggerService : IKeywordTriggerService, IDisposable
{
    #region Fields

    private readonly StringBuilder _buffer = new(200);
    private DateTime _lastKeyTime = DateTime.MinValue;
    private DateTime _lastGlobalTriggerTime = DateTime.MinValue;
    private bool _isActive;
    private bool _disposed;

    private readonly object _audioLock = new();
    private MediaPlayer? _triggerPlayer;
    private Media? _triggerMedia;

    private string[]? _audioFilesCache;
    private DateTime _audioFilesCacheTime = DateTime.MinValue;
    private string[]? _modAudioFilesCache;
    private DateTime _modAudioFilesCacheTime = DateTime.MinValue;
    private string? _modAudioCacheModId;
    private readonly string _audioPath;

    private readonly Dictionary<string, DateTime> _mutedKeywords = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _muteLock = new();

    private const int PulseBufferCapacity = 20;
    private readonly LinkedList<TriggerFireRecord> _recentFires = new();
    private readonly object _recentFiresLock = new();

    private Dictionary<string, int> _ocrSeenCounts = new();
    private readonly HashSet<string> _highlightedOcrKeys = new();

    private readonly ISettingsService _settingsService;
    private readonly IModService _modService;
    private readonly IProgressionService _progressionService;
    private readonly IQuestService _questService;
    private readonly IAchievementService _achievementService;
    private readonly ICompanionService _companionService;
    private readonly ICompanionPhraseService _companionPhraseService;
    private readonly IAiService? _aiService;
    private readonly IAvatarWindowService _avatarWindowService;
    private readonly ISubliminalService _subliminalService;
    private readonly IFlashService _flashService;
    private readonly IOverlayService _overlayService;
    private readonly IMindWipeService _mindWipeService;
    private readonly IBubbleService _bubbleService;
    private readonly IHapticsService? _hapticsService;
    private readonly ISystemAudioDucker? _audioDucker;
    private readonly IAudioDeviceService? _audioDeviceService;
    private readonly ISessionService _sessionService;
    private readonly IEnumerable<IAuthProvider> _authProviders;
    private readonly IInputHook _inputHook;
    private readonly IKeywordHighlightService _highlightService;
    private readonly ILibVlcProvider _libVlcProvider;
    private readonly IAppEnvironment _environment;
    private readonly ILogger<AvaloniaKeywordTriggerService> _logger;

    #endregion

    #region Public API

    public bool IsRunning => _isActive;
    public bool NeedsOcrConfirmation { get; private set; }

    public event EventHandler<KeywordTrigger>? TriggerFired;

    public AvaloniaKeywordTriggerService(
        ISettingsService settingsService,
        IModService modService,
        IProgressionService progressionService,
        IQuestService questService,
        IAchievementService achievementService,
        ICompanionService companionService,
        ICompanionPhraseService companionPhraseService,
        IAvatarWindowService avatarWindowService,
        ISubliminalService subliminalService,
        IFlashService flashService,
        IOverlayService overlayService,
        IMindWipeService mindWipeService,
        IBubbleService bubbleService,
        ISessionService sessionService,
        IInputHook inputHook,
        IKeywordHighlightService highlightService,
        ILibVlcProvider libVlcProvider,
        IAppEnvironment environment,
        ILogger<AvaloniaKeywordTriggerService> logger,
        IAiService? aiService = null,
        IHapticsService? hapticsService = null,
        ISystemAudioDucker? audioDucker = null,
        IAudioDeviceService? audioDeviceService = null,
        IEnumerable<IAuthProvider>? authProviders = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _modService = modService ?? throw new ArgumentNullException(nameof(modService));
        _progressionService = progressionService ?? throw new ArgumentNullException(nameof(progressionService));
        _questService = questService ?? throw new ArgumentNullException(nameof(questService));
        _achievementService = achievementService ?? throw new ArgumentNullException(nameof(achievementService));
        _companionService = companionService ?? throw new ArgumentNullException(nameof(companionService));
        _companionPhraseService = companionPhraseService ?? throw new ArgumentNullException(nameof(companionPhraseService));
        _avatarWindowService = avatarWindowService ?? throw new ArgumentNullException(nameof(avatarWindowService));
        _subliminalService = subliminalService ?? throw new ArgumentNullException(nameof(subliminalService));
        _flashService = flashService ?? throw new ArgumentNullException(nameof(flashService));
        _overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));
        _mindWipeService = mindWipeService ?? throw new ArgumentNullException(nameof(mindWipeService));
        _bubbleService = bubbleService ?? throw new ArgumentNullException(nameof(bubbleService));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _inputHook = inputHook ?? throw new ArgumentNullException(nameof(inputHook));
        _highlightService = highlightService ?? throw new ArgumentNullException(nameof(highlightService));
        _libVlcProvider = libVlcProvider ?? throw new ArgumentNullException(nameof(libVlcProvider));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _aiService = aiService;
        _hapticsService = hapticsService;
        _audioDucker = audioDucker;
        _audioDeviceService = audioDeviceService;
        _authProviders = authProviders ?? Enumerable.Empty<IAuthProvider>();

        _audioPath = Path.Combine(_environment.BaseDirectory, "Resources", "sub_audio");

        _inputHook.KeyPressed += OnInputHookKeyPressed;
    }

    public void Start()
    {
        if (_isActive) return;
        if (!HasAccess())
        {
            _logger.LogDebug("KeywordTriggerService: No access (requires Patreon premium)");
            return;
        }

        _isActive = true;
        _buffer.Clear();
        _logger.LogInformation("KeywordTriggerService started");
    }

    public void Stop()
    {
        if (!_isActive) return;
        _isActive = false;
        _buffer.Clear();
        StopTriggerAudio();
        _logger.LogInformation("KeywordTriggerService stopped");
    }

    public void OnKeyPressed(int vkCode)
    {
        if (!_isActive || _disposed) return;
        if (!OperatingSystem.IsWindows()) return;

        var settings = _settingsService.Current;
        if (settings == null || !settings.KeywordTriggersEnabled) return;

        var now = DateTime.Now;
        if ((now - _lastKeyTime).TotalMilliseconds > settings.KeywordBufferTimeoutMs && _buffer.Length > 0)
            _buffer.Clear();
        _lastKeyTime = now;

        // Map common control keys to a simple character representation.
        char? ch = MapControlKey(vkCode);
        if (ch.HasValue)
        {
            if (ch.Value == '\r') // Enter
            {
                _buffer.Clear();
                return;
            }
            if (ch.Value == '\b') // Backspace
            {
                if (_buffer.Length > 0)
                    _buffer.Remove(_buffer.Length - 1, 1);
                return;
            }
            if (ch.Value == ' ') // Space
            {
                _buffer.Append(' ');
                CheckForMatches();
                return;
            }
            if (ch.Value == '\t' || ch.Value == '\u001b') // Tab / Escape
            {
                _buffer.Clear();
                return;
            }
            _buffer.Append(ch.Value);
            CapBuffer();
            CheckForMatches();
            return;
        }

        var translated = TranslateVkCode(vkCode);
        if (translated == null) return;

        _buffer.Append(translated.Value);
        CapBuffer();
        CheckForMatches();
    }

    public void CheckText(string text)
    {
        if (!_isActive || _disposed) return;
        if (string.IsNullOrEmpty(text)) return;

        var settings = _settingsService.Current;
        if (settings == null || !settings.KeywordTriggersEnabled) return;

        var triggers = settings.KeywordTriggers;
        if (triggers == null || triggers.Count == 0) return;

        var now = DateTime.Now;
        if ((now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds)
            return;

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
            _logger.LogInformation("Keyword trigger fired (text): '{Keyword}' id={Id}", t.Keyword, t.Id);
            RecordFire(t, "Text");
            TriggerFired?.Invoke(this, t);
        }
        _lastGlobalTriggerTime = now;
        _ = DispatchMergedAsync(firedTriggers, null);
    }

    public void CheckOcrWords(List<OcrWordHit> allWords)
    {
        NeedsOcrConfirmation = false;

        if (!_isActive || _disposed) return;
        if (allWords == null || allWords.Count == 0)
        {
            _ocrSeenCounts.Clear();
            _highlightedOcrKeys.Clear();
            return;
        }

        var settings = _settingsService.Current;
        if (settings == null || !settings.KeywordTriggersEnabled) return;

        var triggers = settings.KeywordTriggers;
        if (triggers == null || triggers.Count == 0) return;

        if ((DateTime.Now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds)
            return;

        _logger.LogInformation("OCR tokens ({Total}): {Tokens}",
            allWords.Count,
            string.Join(" ", allWords.Take(40).Select(w => $"\"{Truncate(w.Text, 20)}\"")));

        var matchedWords = new List<OcrWordHit>();
        var firedTriggers = new List<KeywordTrigger>();

        foreach (var trigger in OrderedByPresetPriority(triggers))
        {
            if (!trigger.Enabled || string.IsNullOrEmpty(trigger.Keyword)) continue;
            if (trigger.MatchType == KeywordMatchType.Regex) continue;
            if (IsKeywordMuted(trigger.Keyword)) continue;
            if (trigger.IsOnCooldown) continue;

            var words = FindMatchedWords(trigger.Keyword, allWords);
            if (words != null && words.Count > 0)
            {
                firedTriggers.Add(trigger);
                matchedWords.AddRange(words);
            }
        }

        if (matchedWords.Count == 0 || firedTriggers.Count == 0)
        {
            _ocrSeenCounts.Clear();
            _highlightedOcrKeys.Clear();
            return;
        }

        var effectTrigger = firedTriggers[0];

        const int PositionBucket = 120;
        var currentPositions = new HashSet<string>();
        var wordsByKey = new Dictionary<string, OcrWordHit>();

        foreach (var word in matchedWords)
        {
            var key = $"{word.Text.ToLowerInvariant()}_{(int)word.ScreenRect.X / PositionBucket}_{(int)word.ScreenRect.Y / PositionBucket}";
            if (currentPositions.Add(key))
                wordsByKey[key] = word;
        }

        _highlightedOcrKeys.IntersectWith(currentPositions);

        int requiredScans = Math.Max(1, settings.OcrConfirmationScans);
        var seenCounts = new Dictionary<string, int>(currentPositions.Count);
        bool anyPendingConfirmation = false;
        foreach (var key in currentPositions)
        {
            int streak = (_ocrSeenCounts.TryGetValue(key, out var prior) ? prior : 0) + 1;
            seenCounts[key] = streak;
            if (streak < requiredScans && !_highlightedOcrKeys.Contains(key))
                anyPendingConfirmation = true;
        }
        _ocrSeenCounts = seenCounts;

        NeedsOcrConfirmation = anyPendingConfirmation;

        bool IsConfirmed(string key)
            => seenCounts.TryGetValue(key, out var c) && c >= requiredScans;

        var newWords = new List<OcrWordHit>();
        var newKeys = new HashSet<string>();
        bool highlightAll = settings.OcrHighlightAll;

        if (highlightAll)
        {
            foreach (var kvp in wordsByKey)
            {
                if (!_highlightedOcrKeys.Contains(kvp.Key) && IsConfirmed(kvp.Key))
                {
                    newWords.Add(kvp.Value);
                    newKeys.Add(kvp.Key);
                }
            }
        }
        else
        {
            var candidates = wordsByKey.Where(kvp => !_highlightedOcrKeys.Contains(kvp.Key) && IsConfirmed(kvp.Key)).ToList();
            if (candidates.Count > 0)
            {
                int count = Random.Shared.Next(1, candidates.Count + 1);
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

        _highlightedOcrKeys.UnionWith(newKeys);

        _logger.LogInformation(
            "OCR guard: scan matched {Matched} positions, {New} new, {Guarded} already guarded",
            currentPositions.Count, newKeys.Count, currentPositions.Count - newKeys.Count);

        if (newWords.Count == 0) return;

        _logger.LogInformation("OCR keyword confirmed: [{Keywords}] — {Count} new words across {Triggers} trigger(s)",
            string.Join(",", firedTriggers.Select(t => t.Keyword)), newWords.Count, firedTriggers.Count);

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
            _logger.LogDebug(ex, "FireDemoTrigger failed");
        }
    }

    public List<KeywordTrigger> ImportFromCustomTriggers()
    {
        var customTriggers = _settingsService.Current?.CustomTriggers;
        if (customTriggers == null || customTriggers.Count == 0)
            return new List<KeywordTrigger>();

        var existing = _settingsService.Current?.KeywordTriggers ?? new List<KeywordTrigger>();
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
            RebuildActionsFromFlatFields(kt);
            imported.Add(kt);
        }

        return imported;
    }

    public void PreviewAudioClip(string filePath, int volume)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        var resolved = ResolveAudioPath(filePath);
        if (!File.Exists(resolved))
        {
            _logger.LogWarning("PreviewAudioClip: file not found {Path}", resolved);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await PlayTriggerAudioAsync(resolved, volume);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PreviewAudioClip: failed to play {Path}", resolved);
            }
        });
    }

    public IReadOnlyList<TriggerFireRecord> GetRecentFires()
    {
        lock (_recentFiresLock)
        {
            return _recentFires.ToArray();
        }
    }

    public void MuteKeywordEcho(string text, int muteMs) => ForceMuteKeyword(text, muteMs);

    #endregion

    #region Premium / Audio lookup

    private bool HasAccess()
    {
        return _authProviders.Any(p =>
            string.Equals(p.ProviderName, "patreon", StringComparison.OrdinalIgnoreCase)
            && p.HasPremiumAccess);
    }

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
            cleanText.ToUpper().Replace("\u2019", "'")
        };

        var modPath = _modService.ActiveMod?.InstalledPath;
        if (!string.IsNullOrEmpty(modPath))
        {
            var modAudioDir = Path.Combine(modPath, "resources", "sounds", "flashes_audio");
            if (Directory.Exists(modAudioDir))
            {
                var result = SearchAudioDirectory(modAudioDir, cleanText, textVariants, extensions, isModCache: true);
                if (result != null) return result;
            }
        }

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

        try
        {
            if (Directory.Exists(directory))
            {
                string[]? files;
                if (isModCache)
                {
                    var currentModId = _modService.ActiveMod?.Id;
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
            _logger.LogDebug(ex, "KeywordTriggerService: Error searching audio files in {Dir}", directory);
        }

        return null;
    }

    private static string ResolveAudioPath(string path)
    {
        if (Path.IsPathRooted(path)) return path;

        var resPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", path);
        if (File.Exists(resPath)) return resPath;

        var subPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sub_audio", path);
        if (File.Exists(subPath)) return subPath;

        return path;
    }

    #endregion

    #region Mute / Pulse feed

    private bool IsKeywordMuted(string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return false;
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

    private void ForceMuteKeyword(string keyword, int muteMs)
    {
        if (string.IsNullOrEmpty(keyword) || muteMs <= 0) return;
        lock (_muteLock)
        {
            var expiresAt = DateTime.UtcNow.AddMilliseconds(muteMs);
            if (!_mutedKeywords.TryGetValue(keyword, out var existing) || existing < expiresAt)
                _mutedKeywords[keyword] = expiresAt;
        }
        _logger.LogDebug("ForceMuteKeyword: '{Keyword}' muted for {Ms}ms", keyword, muteMs);
    }

    private void RecordFire(KeywordTrigger trigger, string source)
    {
        var settings = _settingsService.Current;
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
                    if (!_mutedKeywords.TryGetValue(trigger.Keyword, out var existing) || existing < expiresAt)
                        _mutedKeywords[trigger.Keyword] = expiresAt;
                }
            }
        }

        var record = new TriggerFireRecord
        {
            Keyword = trigger.Keyword,
            TriggerId = trigger.Id,
            VisualEffect = trigger.VisualEffect,
            Source = source,
            FiredAt = DateTime.Now,
            ActionKeys = BuildActionKeySnapshot(trigger)
        };
        lock (_recentFiresLock)
        {
            _recentFires.AddFirst(record);
            while (_recentFires.Count > PulseBufferCapacity)
                _recentFires.RemoveLast();
        }
    }

    private static List<string> BuildActionKeySnapshot(KeywordTrigger trigger)
    {
        var keys = new List<string>();
        if (trigger.Actions == null) return keys;
        foreach (var action in trigger.Actions)
        {
            if (action == null || !action.Enabled) continue;
            switch (action)
            {
                case PlayAudioAction: keys.Add("PlayAudio"); break;
                case HighlightAction: keys.Add("Highlight"); break;
                case HapticAction: keys.Add("Haptic"); break;
                case AddXpAction xp: keys.Add($"AddXp:{xp.Amount}"); break;
                case AvatarCommentAction: keys.Add("AvatarComment"); break;
                case ExtendSessionAction ext: keys.Add($"ExtendSession:{ext.Minutes}"); break;
                case ChasterAddTimeAction ch: keys.Add($"ChasterAddTime:{ch.Minutes}"); break;
                case VisualEffectAction ve: keys.Add($"VisualEffect:{ve.Effect}"); break;
            }
        }
        return keys;
    }

    #endregion

    #region Matching

    private static IEnumerable<KeywordTrigger> OrderedByPresetPriority(List<KeywordTrigger> triggers)
    {
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

    private void CheckForMatches()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        var triggers = settings.KeywordTriggers;
        if (triggers == null || triggers.Count == 0) return;

        var now = DateTime.Now;
        if ((now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds)
            return;

        var bufferText = _buffer.ToString();
        if (string.IsNullOrEmpty(bufferText)) return;

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

        _buffer.Clear();
        foreach (var t in firedTriggers)
        {
            t.LastTriggeredAt = now;
            _logger.LogInformation("Keyword trigger fired: '{Keyword}' id={Id}", t.Keyword, t.Id);
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
            return false;
        }
    }

    private static bool ContainsWholeWord(string haystack, string keyword)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(keyword)) return false;
        try
        {
            var parts = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var escaped = string.Join("\\s+", parts.Select(Regex.Escape));
            var pattern = $"\\b{escaped}\\b";
            return Regex.IsMatch(haystack, pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        catch
        {
            return false;
        }
    }

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
            int minX = (int)first.ScreenRect.X;
            int minY = (int)first.ScreenRect.Y;
            int maxRight = (int)first.ScreenRect.X + (int)first.ScreenRect.Width;
            int maxBottom = (int)first.ScreenRect.Y + (int)first.ScreenRect.Height;
            for (int j = 1; j < keywordParts.Length; j++)
            {
                var r = wordHits[i + j].ScreenRect;
                if (r.X < minX) minX = (int)r.X;
                if (r.Y < minY) minY = (int)r.Y;
                int rRight = (int)r.X + (int)r.Width;
                int rBottom = (int)r.Y + (int)r.Height;
                if (rRight > maxRight) maxRight = rRight;
                if (rBottom > maxBottom) maxBottom = rBottom;
            }

            results.Add(new OcrWordHit
            {
                Text = keyword,
                ScreenRect = new PixelRect(minX, minY, maxRight - minX, maxBottom - minY),
                Screen = first.Screen
            });
        }

        return results.Count > 0 ? results : null;
    }

    private static bool IsWholeWordMatch(string ocrToken, string keywordWord)
    {
        if (string.IsNullOrEmpty(ocrToken) || string.IsNullOrEmpty(keywordWord)) return false;
        var stripped = StripEdgePunctuation(ocrToken);
        return stripped.Equals(keywordWord, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripEdgePunctuation(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int start = 0, end = s.Length - 1;
        while (start <= end && !char.IsLetterOrDigit(s[start])) start++;
        while (end >= start && !char.IsLetterOrDigit(s[end])) end--;
        return start > end ? string.Empty : s.Substring(start, end - start + 1);
    }

    #endregion

    #region Dispatch

    private async Task DispatchResponseAsync(KeywordTrigger trigger, List<OcrWordHit>? matchedWords = null)
    {
        try
        {
            if (_disposed) return;

            _questService.TrackKeywordTrigger();

            if (trigger.Actions != null && trigger.Actions.Count > 0)
            {
                await DispatchActionsAsync(trigger, matchedWords);
                return;
            }

            await DispatchLegacyFlatFieldsAsync(trigger, matchedWords);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KeywordTriggerService: Error dispatching response");
        }
    }

    private async Task DispatchMergedAsync(List<KeywordTrigger> firedTriggers, List<OcrWordHit>? matchedWords)
    {
        if (firedTriggers == null || firedTriggers.Count == 0) return;
        var first = firedTriggers[0];

        if (firedTriggers.Count == 1)
        {
            await DispatchResponseAsync(first, matchedWords);
            return;
        }

        foreach (var t in firedTriggers)
            _questService.TrackKeywordTrigger();

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

        _logger.LogInformation("DispatchMerged: {N} trigger(s) [{Keywords}] → {Count} unique actions",
            firedTriggers.Count, string.Join(",", firedTriggers.Select(t => t.Keyword)), merged.Count);

        var synthetic = new KeywordTrigger
        {
            Id = first.Id,
            Keyword = first.Keyword,
            VisualEffect = first.VisualEffect,
            Actions = merged
        };
        await DispatchActionsAsync(synthetic, matchedWords);
    }

    private static string GetActionDedupKey(KeywordAction a) => a switch
    {
        PlayAudioAction => "PlayAudio",
        HighlightAction => "Highlight",
        HapticAction => "Haptic",
        AddXpAction => "AddXp",
        AvatarCommentAction => "AvatarComment",
        VisualEffectAction ve => $"VisualEffect:{ve.Effect}",
        ExtendSessionAction => "ExtendSession",
        ChasterAddTimeAction => "ChasterAddTime",
        _ => a.GetType().Name
    };

    private async Task DispatchActionsAsync(KeywordTrigger trigger, List<OcrWordHit>? matchedWords)
    {
        bool didDuck = false;
        double maxAudioDuration = 0;

        var actions = trigger.Actions?.Where(a => a != null && a.Enabled).ToList() ?? new List<KeywordAction>();
        _logger.LogInformation("DispatchActions '{Kw}' id={Id} actions=[{List}]",
            trigger.Keyword, trigger.Id,
            string.Join(",", actions.Select(a => a.GetType().Name + (a is PlayAudioAction p ? $"({p.FilePath})" : ""))));

        bool anyDuck = actions.OfType<PlayAudioAction>().Any(a => a.DuckSystemAudio);
        if (anyDuck && _settingsService.Current?.AudioDuckingEnabled == true && _audioDucker != null)
        {
            // Pass the user's ducking strength (WPF KeywordTriggerService.cs:1126).
            _audioDucker.Duck(_settingsService.Current?.DuckingLevel ?? 80);
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
            _logger.LogError(ex, "KeywordTriggerService: Action dispatch error");
        }

        if (!didDuck) return;

        if (_disposed || _audioDucker == null)
        {
            _audioDucker?.Unduck();
            return;
        }

        if (maxAudioDuration > 0)
            await Task.Delay(TimeSpan.FromSeconds(maxAudioDuration + 0.5));
        else
            await Task.Delay(500);
        _audioDucker.Unduck();
    }

    private async Task<double> DispatchActionAsync(KeywordAction action, KeywordTrigger trigger, List<OcrWordHit>? matchedWords)
    {
        switch (action)
        {
            case PlayAudioAction audio:
                return await DispatchPlayAudioAsync(audio);

            case VisualEffectAction visual:
                await Dispatcher.UIThread.InvokeAsync(() => FireVisualEffect(visual.Effect, trigger));
                return 0;

            case HighlightAction:
                {
                    var hasMatched = matchedWords != null && matchedWords.Count > 0;
                    var hlEnabled = _settingsService.Current?.KeywordHighlightEnabled == true;
                    _logger.LogInformation("HighlightAction: matchedWords={Count} hlEnabled={Enabled}",
                        hasMatched ? matchedWords!.Count : 0, hlEnabled);
                    if (hasMatched && hlEnabled)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => _highlightService.ShowHighlight(matchedWords!));
                    }
                }
                return 0;

            case HapticAction haptic:
                if (_hapticsService != null)
                    _ = _hapticsService.TestAsync((int)(haptic.Intensity * 100), 500);
                return 0;

            case AddXpAction xp:
                if (xp.Amount > 0)
                {
                    var xpAmount = (double)xp.Amount;
                    if (_sessionService.State == SessionState.Running)
                    {
                        var multiplier = _settingsService.Current?.KeywordSessionMultiplier ?? 1.5;
                        xpAmount *= multiplier;
                    }
                    _progressionService.AddXP((int)xpAmount, XPSource.KeywordTrigger);
                    _achievementService.TrackXPEarned(xpAmount);
                }
                return 0;

            case AvatarCommentAction comment:
                DispatchAvatarComment(comment, trigger);
                return 0;

            case ExtendSessionAction ext:
                _logger.LogInformation("KeywordTriggerService: ExtendSessionAction stubbed (+{Min}m)", ext.Minutes);
                return 0;

            case ChasterAddTimeAction chas:
                _logger.LogInformation("KeywordTriggerService: ChasterAddTimeAction stubbed (+{Min}m)", chas.Minutes);
                return 0;

            default:
                return 0;
        }
    }

    private async Task<double> DispatchPlayAudioAsync(PlayAudioAction audio)
    {
        if (string.IsNullOrEmpty(audio.FilePath)) return 0;

        var resolved = ResolveAudioPath(audio.FilePath);
        if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
        {
            _logger.LogWarning("PlayAudioAction: could not resolve '{Path}' → '{Resolved}'", audio.FilePath, resolved);
            return 0;
        }

        _logger.LogInformation("PlayAudioAction: playing '{Path}' at vol {Vol}% x{Count}",
            resolved, audio.Volume, audio.PlayCount);

        double lastDuration = 0;
        var count = Math.Max(1, audio.PlayCount);
        for (int i = 0; i < count; i++)
        {
            if (_disposed) break;
            if (i > 0 && audio.DelayBetweenMs > 0)
                await Task.Delay(audio.DelayBetweenMs);
            lastDuration = await PlayTriggerAudioAsync(resolved, audio.Volume);
        }

        _logger.LogInformation("PlayAudioAction: returned duration {Dur:0.00}s", lastDuration);
        return lastDuration;
    }

    private async Task<double> PlayTriggerAudioAsync(string path, int volumePercent)
    {
        MediaPlayer? player;
        Media? media;
        lock (_audioLock)
        {
            StopTriggerAudio();
            if (!File.Exists(path))
            {
                _logger.LogWarning("PlayTriggerAudio: file not found '{Path}'", path);
                return 0;
            }
            var libVlc = _libVlcProvider.Value;
            media = new Media(libVlc, path);
            player = new MediaPlayer(libVlc);
            _triggerMedia = media;
            _triggerPlayer = player;
        }

        try
        {
            media.Parse();
            double durationSeconds = media.Duration > 0 ? media.Duration / 1000.0 : 0;

            var volume = volumePercent / 100.0;
            var masterVolume = (_settingsService.Current?.MasterVolume ?? 100) / 100.0;
            var curvedVolume = Math.Max(0.05, Math.Pow(volume * masterVolume, 1.5));
            player.Volume = (int)(curvedVolume * 100);

            try
            {
                var deviceId = _audioDeviceService?.GetDefaultOutputDeviceId();
                if (!string.IsNullOrEmpty(deviceId))
                    player.SetOutputDevice(deviceId);
            }
            catch { }

            player.Play(media);
            _logger.LogInformation("PlayTriggerAudio: playing '{File}' curved={Curve:0.00} master={Master:0.00}",
                Path.GetFileName(path), curvedVolume, masterVolume);

            return durationSeconds;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PlayTriggerAudio: Error playing audio '{Path}'", path);
            return 0;
        }
    }

    private void StopTriggerAudio()
    {
        try
        {
            _triggerPlayer?.Stop();
            _triggerPlayer?.Dispose();
            _triggerMedia?.Dispose();
        }
        catch { }
        _triggerPlayer = null;
        _triggerMedia = null;
    }

    private void DispatchAvatarComment(AvatarCommentAction a, KeywordTrigger trigger)
    {
        var aiAvailable = _aiService?.IsAvailable == true;

        if (a.RequireAiAvailable && !aiAvailable)
        {
            var canned = PickCannedPhrase(a.FallbackPhraseCategory);
            if (!string.IsNullOrEmpty(canned))
                ShowAvatarLine(canned, aiGenerated: false);
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
                bool fromAi = false;
                if (aiAvailable && _aiService != null)
                {
                    line = await _aiService.GetKeywordCommentAsync(keyword, promptTemplate);
                    fromAi = !string.IsNullOrEmpty(line);
                }

                if (string.IsNullOrEmpty(line))
                    line = PickCannedPhrase(fallbackCategory);

                if (!string.IsNullOrEmpty(line))
                    ShowAvatarLine(line, aiGenerated: fromAi);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "KeywordTriggerService: AvatarComment dispatch failed");
            }
        });
    }

    private string? PickCannedPhrase(string? category)
    {
        if (string.IsNullOrEmpty(category)) return null;
        try
        {
            var phrases = _companionPhraseService.GetAllPhrases()
                .Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase) && p.IsEnabled)
                .Select(p => p.Text)
                .ToArray();

            if (phrases.Length == 0)
                phrases = _modService.GetPhrases(category);

            if (phrases == null || phrases.Length == 0) return null;
            return phrases[Random.Shared.Next(phrases.Length)];
        }
        catch
        {
            return null;
        }
    }

    private void ShowAvatarLine(string line, bool aiGenerated) => ShowAvatarLine(line, aiGenerated, attempt: 0);

    private void ShowAvatarLine(string line, bool aiGenerated, int attempt)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var avatar = _avatarWindowService;
                // GigglePriority stops the speech timers and clears the queue, so an awareness comment
                // fired mid-ramble cut the companion off (#463). While she's speaking (bubble OR voice
                // clip), wait and retry rather than routing through the queued path that drops the
                // AI badge/sound. Mirrors WPF KeywordTriggerService.ShowAvatarLine (lot-8 C1).
                if (avatar.IsSpeaking || avatar.IsSpeakingAudio)
                {
                    if (attempt >= 16)   // 16 x 750ms = give up after ~12s of nonstop talking
                    {
                        _logger.LogDebug("Awareness comment dropped - companion busy: {Line}", line);
                        return;
                    }
                    var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
                    retry.Tick += (_, _) =>
                    {
                        retry.Stop();
                        ShowAvatarLine(line, aiGenerated, attempt + 1);
                    };
                    retry.Start();
                    return;
                }

                avatar.GigglePriority(line, playSound: true, aiGenerated: aiGenerated);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "AvatarWindow.GigglePriority failed");
            }
        });
    }

    private async Task DispatchLegacyFlatFieldsAsync(KeywordTrigger trigger, List<OcrWordHit>? matchedWords)
    {
        bool didDuck = false;
        try
        {
            if (_disposed) return;

            if (trigger.VisualEffect == KeywordVisualEffect.HighlightOnly)
            {
                if (matchedWords != null && matchedWords.Count > 0
                    && _settingsService.Current?.KeywordHighlightEnabled == true)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => _highlightService.ShowHighlight(matchedWords));
                }
                return;
            }

            if (trigger.DuckAudio && _settingsService.Current?.AudioDuckingEnabled == true && _audioDucker != null)
            {
                // Pass the user's ducking strength (WPF KeywordTriggerService.cs:1384).
                _audioDucker.Duck(_settingsService.Current?.DuckingLevel ?? 80);
                didDuck = true;
            }

            double audioDuration = 0;
            if (!string.IsNullOrEmpty(trigger.AudioFilePath) && File.Exists(trigger.AudioFilePath))
            {
                for (int i = 0; i < trigger.AudioPlayCount; i++)
                {
                    if (_disposed) break;
                    if (i > 0 && trigger.AudioDelayBetweenMs > 0)
                        await Task.Delay(trigger.AudioDelayBetweenMs);
                    audioDuration = await PlayTriggerAudioAsync(trigger.AudioFilePath, trigger.AudioVolume);
                }
            }

            if (_disposed) return;

            await Dispatcher.UIThread.InvokeAsync(() => FireVisualEffect(trigger.VisualEffect, trigger));

            if (matchedWords != null && matchedWords.Count > 0
                && _settingsService.Current?.KeywordHighlightEnabled == true)
            {
                await Dispatcher.UIThread.InvokeAsync(() => _highlightService.ShowHighlight(matchedWords));
            }

            if (trigger.HapticEnabled && _hapticsService != null)
                _ = _hapticsService.TestAsync((int)(trigger.HapticIntensity * 100), 500);

            if (trigger.XPAward > 0)
            {
                var xpAmount = (double)trigger.XPAward;
                if (_sessionService.State == SessionState.Running)
                {
                    var multiplier = _settingsService.Current?.KeywordSessionMultiplier ?? 1.5;
                    xpAmount *= multiplier;
                }
                _progressionService.AddXP((int)xpAmount, XPSource.KeywordTrigger);
                _achievementService.TrackXPEarned(xpAmount);
            }

            if (_disposed) return;
            if (didDuck)
            {
                if (audioDuration > 0)
                    await Task.Delay(TimeSpan.FromSeconds(audioDuration + 0.5));
                else
                    await Task.Delay(500);
                _audioDucker?.Unduck();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KeywordTriggerService: Legacy dispatch error");
            if (didDuck)
                _audioDucker?.Unduck();
        }
    }

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
                DuckSystemAudio = trigger.DuckAudio
            });
        }

        if (trigger.VisualEffect != KeywordVisualEffect.None &&
            trigger.VisualEffect != KeywordVisualEffect.HighlightOnly)
        {
            list.Add(new VisualEffectAction { Effect = trigger.VisualEffect });
        }

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
                    _subliminalService.FlashSubliminal();
                    break;

                case KeywordVisualEffect.ExactSubliminal:
                    _subliminalService.FlashSubliminalCustom(trigger.Keyword.ToUpperInvariant());
                    ForceMuteKeyword(trigger.Keyword, 3000);
                    break;

                case KeywordVisualEffect.ImageFlash:
                    _flashService.TriggerFlashOnce(null, 1000, false, false);
                    break;

                case KeywordVisualEffect.OverlayPulse:
                    _overlayService.PulseOverlays();
                    break;

                case KeywordVisualEffect.MindWipe:
                    if (_mindWipeService.AudioFileCount > 0)
                        _mindWipeService.TriggerOnce();
                    break;

                case KeywordVisualEffect.Bubbles:
                    _bubbleService.SpawnOnce();
                    break;

                case KeywordVisualEffect.HighlightOnly:
                case KeywordVisualEffect.None:
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "KeywordTriggerService: Error firing visual effect");
        }
    }

    #endregion

    #region Input hook / Key translation

    private void OnInputHookKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        try
        {
            OnKeyPressed(e.VirtualKeyCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception in keyword input-hook handler");
        }
    }

    private static char? MapControlKey(int vkCode)
    {
        return vkCode switch
        {
            0x0D => '\r',   // VK_RETURN
            0x1B => '\u001b', // VK_ESCAPE
            0x09 => '\t',   // VK_TAB
            0x08 => '\b',   // VK_BACK
            0x20 => ' ',    // VK_SPACE
            _ => null
        };
    }

    private static char? TranslateVkCode(int vkCode)
    {
        if (!OperatingSystem.IsWindows()) return null;
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

    private void CapBuffer()
    {
        if (_buffer.Length > 200)
            _buffer.Remove(0, _buffer.Length - 200);
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

    #region Helpers

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length > maxLength ? text[..maxLength] + "…" : text;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inputHook.KeyPressed -= OnInputHookKeyPressed;
        Stop();
        StopTriggerAudio();
    }

    #endregion
}
