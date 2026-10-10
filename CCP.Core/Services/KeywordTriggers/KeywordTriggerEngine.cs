using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.KeywordTriggers
{
    /// <summary>Keys the typed-word buffer treats specially (WPF OnKeyPressed: Return/Escape/Tab clear,
    /// Back deletes one, Space appends and checks).</summary>
    public enum KeywordBufferKey { Clear, Backspace, Space }

    /// <summary>One merged fire: the synthetic trigger WPF DispatchMergedAsync builds (first trigger's
    /// keyword and id, the de-duplicated union of every fired trigger's enabled actions) plus how many
    /// quest credits it earns (one per fired trigger, WPF :1364 / :1412).</summary>
    public sealed record KeywordFire(KeywordTrigger Trigger, IReadOnlyList<KeywordAction> Actions,
        IReadOnlyList<KeywordTrigger> Fired, string Source,
        IReadOnlyList<OcrWordHit>? MatchedWords = null);

    /// <summary>
    /// The platform-free half of WPF <c>Services/KeywordTriggerService.cs</c> (7.1.5): the rolling typed
    /// buffer, whole-word / regex matching, per-trigger and global cooldowns, the temporal mute (loop
    /// protection + the hard per-keyword cooldown), preset-first ordering, the merge de-dup, the pulse
    /// ring buffer and the app-scope gate. Same numbers as WPF. A head feeds it characters (or text) and
    /// performs the actions on <see cref="Dispatch"/>; nothing here draws, plays or touches Win32.
    /// </summary>
    public sealed partial class KeywordTriggerEngine
    {
        public const int BufferCap = 200;
        public const int PulseBufferCapacity = 20;

        private readonly StringBuilder _buffer = new(BufferCap);
        private DateTime _lastKeyTime = DateTime.MinValue;
        private DateTime _lastGlobalTriggerTime = DateTime.MinValue;
        private readonly Dictionary<string, DateTime> _muted = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _muteLock = new();
        private readonly LinkedList<TriggerFireRecord> _recentFires = new();
        private readonly object _firesLock = new();

        /// <summary>WPF HasAccess: the awareness free day, else premium. Checked per key press, as WPF
        /// does (an entitlement can lapse while the hook is live).</summary>
        public Func<bool> HasAccess { get; set; } =
            () => CoreEntitlement.IsFreeToday("awareness") || CoreEntitlement.HasPremium;

        /// <summary>App-scope gate (WPF IsForegroundAppAllowed). The head resolves the foreground
        /// process; null resolver = everything allowed.</summary>
        public Func<ForegroundApp?>? ForegroundResolver { get; set; }

        public Func<DateTime> Now { get; set; } = () => DateTime.Now;
        public Func<AppSettings?> Settings { get; set; } = () => CoreSettings.Current;

        public bool IsActive { get; private set; }

        /// <summary>One trigger fired (WPF TriggerFired): Pavlov, quests, barks listen here.</summary>
        public event Action<KeywordTrigger, string>? TriggerFired;

        /// <summary>The merged action run (WPF DispatchMergedAsync / DispatchResponseAsync).</summary>
        public event Action<KeywordFire>? Dispatch;

        public void Start() { if (IsActive) return; IsActive = true; _buffer.Clear(); }
        public void Stop() { if (!IsActive) return; IsActive = false; _buffer.Clear(); }

        public string BufferText => _buffer.ToString();

        // ------------------------------------------------------------------ keyboard

        /// <summary>WPF OnKeyPressed for a special key.</summary>
        public void OnKey(KeywordBufferKey key)
        {
            if (!PreKey(out _)) return;
            switch (key)
            {
                case KeywordBufferKey.Clear: _buffer.Clear(); return;
                case KeywordBufferKey.Backspace: if (_buffer.Length > 0) _buffer.Remove(_buffer.Length - 1, 1); return;
                case KeywordBufferKey.Space: _buffer.Append(' '); CheckForMatches(); return;
            }
        }

        /// <summary>WPF OnKeyPressed for a translated character.</summary>
        public void OnChar(char ch)
        {
            if (!PreKey(out _)) return;
            _buffer.Append(ch);
            if (_buffer.Length > BufferCap) _buffer.Remove(0, _buffer.Length - BufferCap);
            CheckForMatches();
        }

        /// <summary>The shared front half of WPF OnKeyPressed: active, master on, access, app scope
        /// (drops the buffer in a blocked app), then the buffer timeout.</summary>
        private bool PreKey(out AppSettings? settings)
        {
            settings = Settings();
            if (!IsActive || settings == null || !settings.KeywordTriggersEnabled) return false;
            if (!HasAccess()) return false;
            if (!IsForegroundAppAllowed(settings))
            {
                if (_buffer.Length > 0) _buffer.Clear();
                return false;
            }
            var now = Now();
            if ((now - _lastKeyTime).TotalMilliseconds > settings.KeywordBufferTimeoutMs && _buffer.Length > 0)
                _buffer.Clear();
            _lastKeyTime = now;
            return true;
        }

        private void CheckForMatches()
        {
            var settings = Settings();
            var triggers = settings?.KeywordTriggers;
            if (settings == null || triggers == null || triggers.Count == 0) return;
            var now = Now();
            if ((now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds) return;
            var text = _buffer.ToString();
            if (text.Length == 0) return;

            var fired = Collect(triggers, text);
            if (fired.Count == 0) return;
            _buffer.Clear();   // prevent re-triggering on the same buffer
            Fire(fired, settings, now, "Keyboard");
        }

        // ------------------------------------------------------------------ text (clipboard, OCR line)

        /// <summary>WPF CheckTextForMatches: free text with no positions (clipboard, a whole OCR read).</summary>
        public void CheckTextForMatches(string text, string source = "Text")
        {
            if (!IsActive || string.IsNullOrEmpty(text) || !HasAccess()) return;
            var settings = Settings();
            if (settings == null || !settings.KeywordTriggersEnabled) return;
            var triggers = settings.KeywordTriggers;
            if (triggers == null || triggers.Count == 0) return;
            if (!IsForegroundAppAllowed(settings)) return;
            var now = Now();
            if ((now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds) return;
            var fired = Collect(triggers, text);
            if (fired.Count == 0) return;
            Fire(fired, settings, now, source);
        }

        private List<KeywordTrigger> Collect(List<KeywordTrigger> triggers, string text)
        {
            var fired = new List<KeywordTrigger>();
            foreach (var t in OrderedByPresetPriority(triggers))
            {
                if (t == null || !t.Enabled || string.IsNullOrEmpty(t.Keyword)) continue;
                if ((Now() - t.LastTriggeredAt).TotalSeconds < t.CooldownSeconds) continue;
                if (IsKeywordMuted(t.Keyword)) continue;
                var matched = t.MatchType == KeywordMatchType.Regex
                    ? TryRegexMatch(text, t.Keyword)
                    : ContainsWholeWord(text, t.Keyword);
                if (matched) fired.Add(t);
            }
            return fired;
        }

        private void Fire(List<KeywordTrigger> fired, AppSettings settings, DateTime now, string source)
        {
            foreach (var t in fired)
            {
                t.LastTriggeredAt = now;
                RecordFire(t, source, settings);
                try { TriggerFired?.Invoke(t, source); } catch { /* a listener never stops the fire */ }
            }
            _lastGlobalTriggerTime = now;
            try { Dispatch?.Invoke(Merge(fired, source)); } catch { /* the head logs its own failures */ }
        }

        /// <summary>WPF FireDemoTrigger: the tutorial's fire. Bypasses cooldowns, mutes and app scope;
        /// the keyword need not be in the settings list.</summary>
        public void FireDemo(string keyword, string source = "Tutorial")
        {
            var settings = Settings();
            var t = settings?.KeywordTriggers?.FirstOrDefault(x =>
                string.Equals(x?.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
            if (t == null)
            {
                t = new KeywordTrigger { Keyword = keyword, VisualEffect = KeywordVisualEffect.SubliminalFlash };
                t.RebuildActionsFromFlatFields();
            }
            if (settings != null) RecordFire(t, source, settings);
            try { TriggerFired?.Invoke(t, source); } catch { }
            try { Dispatch?.Invoke(Merge(new List<KeywordTrigger> { t }, source)); } catch { }
        }

        /// <summary>WPF DispatchMergedAsync's merge: the first trigger's identity with the de-duplicated
        /// union of every trigger's enabled actions. A trigger with no actions list runs its flat fields
        /// (WPF's legacy fallback) without the stored trigger being rewritten.</summary>
        public static KeywordFire Merge(IReadOnlyList<KeywordTrigger> fired, string source)
        {
            var first = fired[0];
            var merged = new List<KeywordAction>();
            var seen = new HashSet<string>();
            foreach (var t in fired)
            {
                foreach (var a in ActionsOf(t))
                {
                    if (a == null || !a.Enabled) continue;
                    if (seen.Add(DedupKey(a))) merged.Add(a);
                }
            }
            var synthetic = new KeywordTrigger
            {
                Id = first.Id, Keyword = first.Keyword, VisualEffect = first.VisualEffect, Actions = merged,
            };
            return new KeywordFire(synthetic, merged, fired, source);
        }

        private static IEnumerable<KeywordAction> ActionsOf(KeywordTrigger t)
        {
            if (t.Actions != null && t.Actions.Count > 0) return t.Actions;
            var copy = new KeywordTrigger
            {
                Keyword = t.Keyword, AudioFilePath = t.AudioFilePath, AudioVolume = t.AudioVolume,
                AudioPlayCount = t.AudioPlayCount, AudioDelayBetweenMs = t.AudioDelayBetweenMs, DuckAudio = t.DuckAudio,
                VisualEffect = t.VisualEffect, HapticEnabled = t.HapticEnabled, HapticIntensity = t.HapticIntensity,
                XPAward = t.XPAward,
            };
            copy.RebuildActionsFromFlatFields();
            return copy.Actions ?? new List<KeywordAction>();
        }

        /// <summary>WPF GetActionDedupKey.</summary>
        public static string DedupKey(KeywordAction a) => a switch
        {
            PlayAudioAction => "PlayAudio",
            HighlightAction => "Highlight",
            HapticAction => "Haptic",
            AddXpAction => "AddXp",
            AvatarCommentAction => "AvatarComment",
            VisualEffectAction ve => $"VisualEffect:{ve.Effect}",
            ExtendSessionAction => "ExtendSession",
            ChasterAddTimeAction => "ChasterAddTime",
            _ => a.GetType().Name,
        };

        // ------------------------------------------------------------------ mutes + pulse feed

        public bool IsKeywordMuted(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return false;
            lock (_muteLock)
            {
                if (_muted.TryGetValue(keyword, out var until))
                {
                    if (DateTime.UtcNow < until) return true;
                    _muted.Remove(keyword);
                }
                return false;
            }
        }

        /// <summary>WPF ForceMuteKeyword / MuteKeywordEcho: extend, never shrink.</summary>
        public void MuteKeywordEcho(string keyword, int muteMs)
        {
            if (string.IsNullOrEmpty(keyword) || muteMs <= 0) return;
            var until = DateTime.UtcNow.AddMilliseconds(muteMs);
            lock (_muteLock)
            {
                if (!_muted.TryGetValue(keyword, out var existing) || existing < until) _muted[keyword] = until;
            }
        }

        private void RecordFire(KeywordTrigger t, string source, AppSettings s)
        {
            if (!string.IsNullOrEmpty(t.Keyword))
            {
                var loopMs = s.AwarenessLoopProtectionEnabled ? s.AwarenessLoopProtectionMs : 0;
                var hardMs = s.KeywordPerKeywordCooldownSeconds * 1000;
                MuteKeywordEcho(t.Keyword, Math.Max(loopMs, hardMs));
            }
            var record = new TriggerFireRecord
            {
                Keyword = t.Keyword, TriggerId = t.Id, VisualEffect = t.VisualEffect, Source = source,
                FiredAt = DateTime.Now, ActionKeys = ActionKeySnapshot(t),
            };
            lock (_firesLock)
            {
                _recentFires.AddFirst(record);
                while (_recentFires.Count > PulseBufferCapacity) _recentFires.RemoveLast();
            }
        }

        public IReadOnlyList<TriggerFireRecord> GetRecentFires()
        {
            lock (_firesLock) return _recentFires.ToArray();
        }

        /// <summary>WPF BuildActionKeySnapshot (AddXp deliberately left out: not a visible effect).</summary>
        public static List<string> ActionKeySnapshot(KeywordTrigger t)
        {
            var keys = new List<string>();
            if (t.Actions == null) return keys;
            foreach (var a in t.Actions)
            {
                if (a == null || !a.Enabled) continue;
                switch (a)
                {
                    case PlayAudioAction: keys.Add("PlayAudio"); break;
                    case HighlightAction: keys.Add("Highlight"); break;
                    case HapticAction: keys.Add("Haptic"); break;
                    case AvatarCommentAction: keys.Add("AvatarComment"); break;
                    case ExtendSessionAction ext: keys.Add($"ExtendSession:{ext.Minutes}"); break;
                    case ChasterAddTimeAction ch: keys.Add($"ChasterAddTime:{ch.Minutes}"); break;
                    case VisualEffectAction ve: keys.Add($"VisualEffect:{ve.Effect}"); break;
                }
            }
            return keys;
        }

        // ------------------------------------------------------------------ app scope

        private bool IsForegroundAppAllowed(AppSettings s)
        {
            var scope = s.KeywordTriggerAppScope;
            if (scope == AwarenessAppScope.Everywhere && !s.KeywordTriggerIgnoreOwnFocus) return true;
            var app = ForegroundResolver?.Invoke();
            if (app == null) return scope != AwarenessAppScope.OnlyListed;   // allow list fails closed
            if (app.IsOwnProcess && s.KeywordTriggerIgnoreOwnFocus) return false;
            return scope switch
            {
                AwarenessAppScope.ExceptListed => !MatchesAppList(s.KeywordTriggerApps, app.ProcessName),
                AwarenessAppScope.OnlyListed => MatchesAppList(s.KeywordTriggerApps, app.ProcessName),
                _ => true,
            };
        }

        public static bool MatchesAppList(IEnumerable<string>? list, string processName)
        {
            if (list == null || string.IsNullOrWhiteSpace(processName)) return false;
            foreach (var raw in list)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var entry = raw.Trim();
                if (entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) entry = entry[..^4];
                if (entry.Length == 0) continue;
                if (string.Equals(entry, processName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>WPF ParseAppList: split on , ; newline, drop ".exe", de-duplicate case-insensitively.</summary>
        public static List<string> ParseAppList(string? text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var part in text.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var entry = part.Trim();
                if (entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) entry = entry[..^4];
                if (entry.Length == 0) continue;
                if (result.Any(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(entry);
            }
            return result;
        }

        // ------------------------------------------------------------------ matching

        /// <summary>Installed preset clones (id "preset:") first, then custom triggers (WPF).</summary>
        public static IEnumerable<KeywordTrigger> OrderedByPresetPriority(List<KeywordTrigger> triggers)
        {
            foreach (var t in triggers) if (t?.Id?.StartsWith("preset:", StringComparison.Ordinal) == true) yield return t;
            foreach (var t in triggers) if (t?.Id?.StartsWith("preset:", StringComparison.Ordinal) != true) yield return t;
        }

        /// <summary>Whole-word, case-insensitive; multi-word keywords join on \s+ (WPF ContainsWholeWord).</summary>
        public static bool ContainsWholeWord(string haystack, string keyword)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(keyword)) return false;
            try
            {
                var parts = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var pattern = "\\b" + string.Join("\\s+", parts.Select(Regex.Escape)) + "\\b";
                return Regex.IsMatch(haystack, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100));
            }
            catch { return false; }
        }

        public static bool TryRegexMatch(string input, string pattern)
        {
            try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
            catch { return false; }   // a bad pattern is silent, as WPF
        }

        // ------------------------------------------------------------------ editor helpers

        /// <summary>WPF BtnAddKeywordTrigger_Click's new row.</summary>
        public static KeywordTrigger NewCustomTrigger(string keyword = "", string? audio = null)
        {
            var t = new KeywordTrigger
            {
                Keyword = keyword, MatchType = KeywordMatchType.PlainText, Enabled = true, CooldownSeconds = 30,
                AudioFilePath = audio, AudioVolume = 80, VisualEffect = KeywordVisualEffect.SubliminalFlash,
                HapticEnabled = true, HapticIntensity = 0.5, DuckAudio = true, XPAward = 10,
            };
            t.RebuildActionsFromFlatFields();
            return t;
        }

        /// <summary>WPF ImportFromCustomTriggers: every Trigger Mode phrase not already a keyword.</summary>
        public static List<KeywordTrigger> ImportFromCustomTriggers(AppSettings s, Func<string, string?>? findAudio = null)
        {
            var imported = new List<KeywordTrigger>();
            if (s.CustomTriggers == null || s.CustomTriggers.Count == 0) return imported;
            var existing = new HashSet<string>((s.KeywordTriggers ?? new()).Select(t => (t.Keyword ?? "").ToUpperInvariant()));
            foreach (var phrase in s.CustomTriggers)
            {
                if (string.IsNullOrWhiteSpace(phrase) || existing.Contains(phrase.ToUpperInvariant())) continue;
                // WPF's import leaves Actions empty (the flat fields drive the legacy dispatch); building
                // them here gives the same effects and lets the editor rows read one list.
                imported.Add(NewCustomTrigger(phrase, findAudio?.Invoke(phrase)));
            }
            return imported;
        }
    }

    /// <summary>The foreground window's process, as the head resolves it (WPF TryResolveForegroundApp).</summary>
    public sealed record ForegroundApp(string ProcessName, bool IsOwnProcess);
}
