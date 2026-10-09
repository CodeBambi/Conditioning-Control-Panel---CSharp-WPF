// PORTED from AvatarTube/AvatarTubeWindow.CirceEmotes.cs (WPF 7.1.5): the speech half of the emote
// avatar - :571 CircePlayEmote, :603 StartTalkSequence, :660 StartReactionOnly, :720 IsNonverbal,
// :783 TalkTiming, :822 ResolveReaction, :851 PlanTalk - and Avatar.cs:1265 EstimateDurationSec.
// Ledger row tube#T3. Same emotes.json keys (talking.pool / maxTalkClips, expressive.pool /
// nonverbalMaxSec, talkStartDelayMs, talkLeadOutMs, minClipMs, stemPrefix, mood) and clip_timing.json.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private const int NonverbalLeadInMs = 250;
        private const int MinTalkWindowMs = 500;
        private const int TalkFallbackLenMs = 2500;
        private const int TalkFastStartMs = 700;

        private string? _talkMapFolder;   // the emote folder the talk map was read from
        private readonly List<string> _talkPool = new();
        private readonly List<string> _expressivePool = new();
        private readonly Dictionary<string, string> _stemPrefix = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _moodMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (int Start, int End, int Dur)> _talkTiming = new(StringComparer.OrdinalIgnoreCase);
        private int _maxTalkClips = 3, _talkStartDelayMs = 1000, _talkLeadOutMs = 500, _minClipMs = 2500;
        private double _nonverbalMaxSec = 1.4;

        private bool _talkSeqActive;
        private readonly Queue<(long At, string Clip, bool IsReaction)> _talkSchedule = new();
        private long _talkSeqStart;
        private DispatcherTimer? _talkTimer, _talkStartTimer;
        private int _nextAudioLeadInMs;

        /// <summary>True while a spoken line's talk clips own the avatar (tests).</summary>
        internal bool TalkSequenceActive => _talkSeqActive;

        /// <summary>WPF EmoteAudioLeadInSeconds: ~0 with an animated set (voice first), else the flat lead-in.</summary>
        internal double EmoteAudioLeadInSeconds(double fallbackSec) =>
            _emoteMode ? _nextAudioLeadInMs / 1000.0 : fallbackSec;

        /// <summary>WPF Avatar.cs:1265: 0.45 s a word + 0.8, clamped 2..7 s (2.5 s for no text).</summary>
        internal static double EstimateDurationSec(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 2.5;
            int words = text!.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return Math.Clamp(0.45 * words + 0.8, 2.0, 7.0);
        }

        private static readonly Regex NonverbalRe =
            new(@"^(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+)(?:[\s,.~!\-]+(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+))*[\s,.~!\-]*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>WPF IsNonverbal: "*giggles*", "mmm~", "hehe"... get one expressive clip, no mouth flaps.</summary>
        internal static bool IsNonverbalLine(string? text) =>
            !string.IsNullOrWhiteSpace(text) && NonverbalRe.IsMatch(text!.Trim());

        /// <summary>ShowGiggle's hook (WPF PlayEmotionForLine -> CircePlayEmote): plan talk clips for the line.</summary>
        private void NoteLineStarting(string text, string? audioPath, SpeechSource source)
        {
            _nextAudioLeadInMs = 0;
            if (!_emoteMode) return;
            try
            {
                EnsureTalkMap();
                // ponytail: WPF reads the clip's real length (AudioDurationSec); this head has no
                // duration probe, so every line is sized from its words, as WPF's text-only lines are.
                double durationSec = EstimateDurationSec(text);
                if (IsNonverbalLine(text))
                {
                    StartReactionOnly(PickExpressive() ?? PickWeightedIdle(), NonverbalLeadInMs);
                    return;
                }
                var stem = audioPath != null ? Path.GetFileNameWithoutExtension(audioPath) : null;
                var reaction = ResolveReaction(stem, null) ?? PickExpressive() ?? PickWeightedIdle();
                StartTalkSequence(PlanTalk(durationSec), reaction, durationSec);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Talk sequence failed"); }
        }

        /// <summary>The bubble went away: nothing more to do (the reaction plays once, then idle).</summary>
        private void NoteLineEnded() { }

        private void EnsureTalkMap()
        {
            if (_talkMapFolder == _emoteFolder) return;
            _talkMapFolder = _emoteFolder;
            _talkPool.Clear(); _expressivePool.Clear(); _stemPrefix.Clear(); _moodMap.Clear(); _talkTiming.Clear();
            _maxTalkClips = 3; _talkStartDelayMs = 1000; _talkLeadOutMs = 500; _minClipMs = 2500; _nonverbalMaxSec = 1.4;
            var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using (var s = OpenEmoteFile("emotes.json"))
            {
                if (s == null) return;
                using var doc = JsonDocument.Parse(s, opts);
                var j = doc.RootElement;
                if (j.TryGetProperty("talking", out var t) && t.ValueKind == JsonValueKind.Object)
                {
                    ReadPool(t, "pool", _talkPool);
                    _maxTalkClips = Int(t, "maxTalkClips") ?? 3;
                }
                if (j.TryGetProperty("expressive", out var e) && e.ValueKind == JsonValueKind.Object)
                {
                    ReadPool(e, "pool", _expressivePool);
                    _nonverbalMaxSec = Dbl(e, "nonverbalMaxSec") ?? 1.4;
                }
                _talkStartDelayMs = Int(j, "talkStartDelayMs") ?? 1000;
                _talkLeadOutMs = Int(j, "talkLeadOutMs") ?? 500;
                _minClipMs = Int(j, "minClipMs") ?? 2500;
                ReadMap(j, "stemPrefix", _stemPrefix);
                ReadMap(j, "mood", _moodMap);
            }
            _talkPool.RemoveAll(c => !EmoteClipExists(c));
            _expressivePool.RemoveAll(c => !EmoteClipExists(c));
            using (var s = OpenEmoteFile("clip_timing.json"))
            {
                if (s == null) return;
                using var doc = JsonDocument.Parse(s, opts);
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Object)
                        _talkTiming[p.Name] = (Int(p.Value, "speakStartMs") ?? 0, Int(p.Value, "speakEndMs") ?? 0, Int(p.Value, "durationMs") ?? 0);
            }

            static void ReadPool(JsonElement o, string key, List<string> into)
            {
                if (o.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array)
                    foreach (var it in a.EnumerateArray())
                        if (it.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(it.GetString())) into.Add(it.GetString()!);
            }
            static void ReadMap(JsonElement o, string key, Dictionary<string, string> into)
            {
                if (o.TryGetProperty(key, out var m) && m.ValueKind == JsonValueKind.Object)
                    foreach (var p in m.EnumerateObject())
                        if (!p.Name.StartsWith('_') && p.Value.ValueKind == JsonValueKind.String) into[p.Name] = p.Value.GetString()!;
            }
        }

        private (int Start, int End, int Dur) TalkTiming(string clip)
        {
            if (_talkTiming.TryGetValue(clip, out var t))
            {
                int dur = t.Dur > 0 ? t.Dur : TalkFallbackLenMs;
                int start = Math.Clamp(t.Start, 0, Math.Max(0, dur - 1));
                int end = t.End > start ? Math.Min(t.End, dur) : dur;
                return (start, end, dur);
            }
            return (0, TalkFallbackLenMs, TalkFallbackLenMs);
        }

        private int TalkLenMs(string clip) { var t = TalkTiming(clip); return t.End - t.Start; }

        private string? ResolveReaction(string? emotionLineId, string? mood)
        {
            if (!string.IsNullOrEmpty(emotionLineId))
            {
                var stem = emotionLineId!.ToLowerInvariant();
                foreach (var kv in _stemPrefix)
                    if (stem.StartsWith(kv.Key, StringComparison.Ordinal) && EmoteClipExists(kv.Value)) return kv.Value;
            }
            if (!string.IsNullOrWhiteSpace(mood) && _moodMap.TryGetValue(mood!.Split(',')[0].Trim(), out var clip) && EmoteClipExists(clip))
                return clip;
            return null;
        }

        private string? PickExpressive()
        {
            var pool = _expressivePool.Where(c => !_emoteBadClips.Contains(c) && c != _emoteCurrentClip).ToList();
            if (pool.Count == 0) pool = _expressivePool.Where(c => !_emoteBadClips.Contains(c)).ToList();
            return pool.Count == 0 ? null : pool[_random.Next(pool.Count)];
        }

        /// <summary>WPF PlanTalk: a snappy first clip, then distinct clips until the line is covered (max N).</summary>
        internal List<string> PlanTalk(double durationSec)
        {
            var pool = _talkPool.Where(c => !_emoteBadClips.Contains(c)).ToList();
            if (pool.Count == 0) return new List<string>();
            int vms = (int)Math.Round(Math.Max(0, durationSec) * 1000);
            var snappy = pool.Where(c => TalkTiming(c).Start <= TalkFastStartMs).ToList();
            string first = snappy.Count > 0 ? snappy[_random.Next(snappy.Count)] : pool.OrderBy(c => TalkTiming(c).Start).First();
            var picks = new List<string> { first };
            int covered = TalkLenMs(first);
            var rest = pool.Where(c => c != first).OrderBy(_ => _random.Next()).ToList();
            foreach (var c in rest)
            {
                if (covered >= vms || picks.Count >= Math.Max(1, _maxTalkClips)) break;
                picks.Add(c);
                covered += TalkLenMs(c);
            }
            return picks;
        }

        private int CurrentClipRemainingHold()
        {
            if (_emoteCurrentClip == null) return 0;
            long elapsed = _emoteWatch.ElapsedMilliseconds - _emoteClipStartMs;
            return (int)Math.Clamp(_minClipMs - elapsed, 0, _minClipMs);
        }

        private void StartTalkSequence(List<string> talk, string reaction, double durationSec)
        {
            StopTalkSequence();
            if (talk.Count == 0) { StartReactionOnly(reaction, 0); return; }
            _nextAudioLeadInMs = 0;   // voice first
            int vms = (int)Math.Round(Math.Max(0, durationSec) * 1000);
            int startDelay = Math.Max(CurrentClipRemainingHold(), _talkStartDelayMs);
            if (vms - startDelay - _talkLeadOutMs < MinTalkWindowMs) { StartReactionOnly(reaction, 0); _nextAudioLeadInMs = 0; return; }

            DeferTalk(() =>
            {
                _talkSeqActive = true;
                _talkSchedule.Clear();
                long deadline = vms - _talkLeadOutMs - startDelay;
                long coveredEnd = TalkTiming(talk[0]).End, lastStart = 0;
                for (int i = 1; i < talk.Count; i++)
                {
                    var ti = TalkTiming(talk[i]);
                    long at = Math.Max(lastStart + _minClipMs, coveredEnd - ti.Start);
                    if (at >= deadline) break;
                    _talkSchedule.Enqueue((at, talk[i], false));
                    lastStart = at;
                    coveredEnd = at + ti.End;
                }
                _talkSchedule.Enqueue((deadline, reaction, true));
                _talkSeqStart = _emoteWatch.ElapsedMilliseconds;
                DoEmoteCrossfade(talk[0]);
                ArmTalkTimer();
            }, startDelay);
        }

        private void StartReactionOnly(string clip, int leadInMs)
        {
            StopTalkSequence();
            int defer = CurrentClipRemainingHold();
            _nextAudioLeadInMs = Math.Clamp(defer + leadInMs, 0, 3000);
            if (defer > 0) DeferTalk(() => DoEmoteCrossfade(clip), defer); else DoEmoteCrossfade(clip);
        }

        private void DeferTalk(Action begin, int ms)
        {
            _talkStartTimer?.Stop();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
            t.Tick += (_, _) => { t.Stop(); if (_talkStartTimer == t) _talkStartTimer = null; if (_emoteMode) begin(); };
            _talkStartTimer = t;
            t.Start();
        }

        private void ArmTalkTimer()
        {
            if (_talkSchedule.Count == 0) { _talkSeqActive = false; return; }
            long wait = Math.Max(1, _talkSchedule.Peek().At - (_emoteWatch.ElapsedMilliseconds - _talkSeqStart));
            _talkTimer?.Stop();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(wait) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (!_emoteMode || !_talkSeqActive || _talkSchedule.Count == 0) { _talkSeqActive = false; return; }
                var ev = _talkSchedule.Dequeue();
                if (ev.IsReaction) _talkSeqActive = false;   // plays once, then the idle rotation
                DoEmoteCrossfade(ev.Clip);
                if (_talkSeqActive) ArmTalkTimer();
            };
            _talkTimer = t;
            t.Start();
        }

        private void StopTalkSequence()
        {
            _talkStartTimer?.Stop(); _talkStartTimer = null;
            _talkTimer?.Stop(); _talkTimer = null;
            _talkSchedule.Clear();
            _talkSeqActive = false;
        }
    }
}
