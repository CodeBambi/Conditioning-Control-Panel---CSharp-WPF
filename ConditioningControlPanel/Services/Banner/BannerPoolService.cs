using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Banner
{
    /// <summary>One authored banner line. <c>cond</c> is optional.</summary>
    public sealed class BannerPoolLine
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("text")] public string Text { get; set; } = "";
        [JsonProperty("cond")] public BannerPoolCond? Cond { get; set; }
    }

    /// <summary>Numeric gate on one live ledger key. Every supplied bound must hold.</summary>
    public sealed class BannerPoolCond
    {
        [JsonProperty("key")] public string Key { get; set; } = "";
        [JsonProperty("gte")] public int? Gte { get; set; }
        [JsonProperty("lte")] public int? Lte { get; set; }
        [JsonProperty("eq")] public int? Eq { get; set; }
    }

    /// <summary>The file as authored: three named buckets.</summary>
    public sealed class BannerPoolFile
    {
        [JsonProperty("taglines")] public List<BannerPoolLine> Taglines { get; set; } = new();
        [JsonProperty("trivia")] public List<BannerPoolLine> Trivia { get; set; } = new();
        [JsonProperty("reads")] public List<BannerPoolLine> Reads { get; set; } = new();
    }

    /// <summary>
    /// The header banner's rotating line pool. Loads <c>Resources/banner/lines.&lt;lang&gt;.json</c>
    /// (whole-file English fallback: a missing or unreadable language file means the English pool,
    /// never a half-translated mix), then hands out one eligible line at a time.
    ///
    /// <para>Eligibility is deliberately strict. A line is only ever shown when every number it
    /// prints is one we actually hold: a <c>cond</c> that fails, an unknown key, or a key that
    /// resolves to zero all take the line out of the draw. The banner never guesses a figure at
    /// the user.</para>
    ///
    /// <para>Buckets are weighted 40 taglines / 35 trivia / 25 reads, two reads never land back to
    /// back, and unseen lines are preferred until a bucket is exhausted, at which point only that
    /// bucket's seen entries are forgotten (see <see cref="AppSettings.ResetBannerSeen"/>).</para>
    ///
    /// <para>Pure logic and disk: no timers, no UI. MainWindow.Marquee.cs owns the beat.</para>
    /// </summary>
    public sealed class BannerPoolService
    {
        private const string Taglines = "taglines";
        private const string Trivia = "trivia";
        private const string Reads = "reads";

        private static readonly (string Bucket, int Weight)[] BucketWeights =
        {
            (Taglines, 40), (Trivia, 35), (Reads, 25)
        };

        private readonly Random _rng = new();
        private readonly object _gate = new();

        private BannerPoolFile? _pool;
        private string _loadedLanguage = "";
        private string _lastBucket = "";

        /// <summary>
        /// Pick the next line to show, or null when nothing is eligible (no pool file, everything
        /// gated out, or the user turned the pool off). The chosen id is recorded as seen.
        /// </summary>
        public string? NextLine()
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null || !settings.BannerPoolEnabled) return null;

                lock (_gate)
                {
                    EnsureLoaded();
                    if (_pool == null) return null;

                    foreach (var bucket in DrawOrder())
                    {
                        var picked = PickFrom(bucket, settings);
                        if (picked == null) continue;

                        _lastBucket = bucket;
                        settings.RecordBannerSeen(picked.Value.Line.Id);
                        App.Settings?.Save();
                        App.Logger?.Debug("[BannerPool] line {Id}", picked.Value.Line.Id);
                        return picked.Value.Text;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[BannerPool] NextLine failed: {E}", ex.Message);
            }
            return null;
        }

        // ------------------------------------------------------------------ picking

        /// <summary>
        /// The weighted bucket first, then the others behind it as fallbacks, so an exhausted or
        /// fully gated bucket still yields a line instead of a blank beat. "reads" is dropped from
        /// the draw entirely when it just went out, which is the whole no-two-reads-in-a-row rule.
        /// </summary>
        private List<string> DrawOrder()
        {
            var pool = new List<(string Bucket, int Weight)>();
            foreach (var bw in BucketWeights)
            {
                if (bw.Bucket == Reads && _lastBucket == Reads) continue;
                pool.Add(bw);
            }
            if (pool.Count == 0) pool.Add((Taglines, 1));

            var order = new List<string>();
            while (pool.Count > 0)
            {
                int total = 0;
                foreach (var bw in pool) total += bw.Weight;
                int roll = _rng.Next(Math.Max(1, total));
                int i = 0;
                for (; i < pool.Count - 1; i++)
                {
                    roll -= pool[i].Weight;
                    if (roll < 0) break;
                }
                order.Add(pool[i].Bucket);
                pool.RemoveAt(i);
            }
            return order;
        }

        /// <summary>
        /// One bucket's draw: eligible lines only, unseen preferred, and when every eligible line
        /// in the bucket has been seen the bucket's seen entries are dropped and it starts over.
        /// </summary>
        private (BannerPoolLine Line, string Text)? PickFrom(string bucket, AppSettings settings)
        {
            var lines = bucket switch
            {
                Taglines => _pool!.Taglines,
                Trivia => _pool!.Trivia,
                _ => _pool!.Reads
            };
            if (lines == null || lines.Count == 0) return null;

            var eligible = new List<(BannerPoolLine Line, string Text)>();
            var unseen = new List<(BannerPoolLine Line, string Text)>();
            foreach (var line in lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.Id) || string.IsNullOrWhiteSpace(line.Text)) continue;
                if (!Passes(line.Cond, settings)) continue;
                var text = Render(line.Text, settings);
                if (text == null) continue;

                var entry = (line, text);
                eligible.Add(entry);
                if (!settings.BannerSeenIds.Contains(line.Id)) unseen.Add(entry);
            }
            if (eligible.Count == 0) return null;

            if (unseen.Count == 0)
            {
                // Bucket exhausted: forget only its own ids, and everything is unseen again.
                settings.ResetBannerSeen(BucketPrefix(bucket));
                unseen = eligible;
            }
            return unseen[_rng.Next(unseen.Count)];
        }

        private static string BucketPrefix(string bucket) => bucket switch
        {
            Taglines => "tg_",
            Trivia => "tv_",
            _ => "rd_"
        };

        // ------------------------------------------------------------------ conditions + tokens

        private static bool Passes(BannerPoolCond? cond, AppSettings settings)
        {
            if (cond == null) return true;
            var value = Resolve(cond.Key, settings);
            if (value == null) return false;
            if (cond.Gte.HasValue && value < cond.Gte.Value) return false;
            if (cond.Lte.HasValue && value > cond.Lte.Value) return false;
            if (cond.Eq.HasValue && value != cond.Eq.Value) return false;
            return true;
        }

        /// <summary>
        /// Substitute every <c>{key}</c>. Returns null (line ineligible) the moment a token names a
        /// key we do not hold or one that reads zero: a line built around a number is a lie without
        /// the number.
        /// </summary>
        private static string? Render(string text, AppSettings settings)
        {
            if (text.IndexOf('{') < 0) return text;

            var sb = new StringBuilder(text.Length + 8);
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] != '{') { sb.Append(text[i++]); continue; }

                int close = text.IndexOf('}', i + 1);
                if (close < 0) { sb.Append(text[i++]); continue; }

                var key = text.Substring(i + 1, close - i - 1);
                var value = Resolve(key, settings);
                if (value == null || value.Value == 0) return null;

                sb.Append(value.Value.ToString(System.Globalization.CultureInfo.CurrentCulture));
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>The live ledger keys, read straight off settings. An unknown key returns null.</summary>
        private static int? Resolve(string? key, AppSettings s) => key switch
        {
            "sessions_7d" => s.SessionsWithinDays(7),
            "late_sessions_7d" => s.LateSessionsWithinDays(7),
            "sessions_today" => s.SessionsToday(),
            "same_mod_run" => s.SameModRun,
            "current_streak" => s.CurrentStreak,
            "total_sessions" => s.TotalSessions,
            "player_level" => s.PlayerLevel,
            _ => null
        };

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// Loads (or reloads, when the UI language changed) the pool file. Failures leave the pool
        /// null and the beat simply never joins the rotation.
        /// </summary>
        private void EnsureLoaded()
        {
            var lang = Localization.LocalizationManager.Instance?.CurrentLanguage ?? "en";
            if (_pool != null && _loadedLanguage == lang) return;

            _loadedLanguage = lang;
            _pool = ReadFile(lang) ?? (lang == "en" ? null : ReadFile("en"));
            if (_pool == null) App.Logger?.Debug("[BannerPool] no pool file for {Lang}", lang);
        }

        private static BannerPoolFile? ReadFile(string lang)
        {
            try
            {
                var path = FindPoolFile("lines." + lang + ".json");
                if (path == null) return null;
                return JsonConvert.DeserializeObject<BannerPoolFile>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[BannerPool] {Lang} pool unreadable: {E}", lang, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The shipped file sits beside the exe (csproj Content item). The walk-up probe is for the
        /// test host, whose base directory is the test project's bin folder.
        /// </summary>
        private static string? FindPoolFile(string fileName)
        {
            try
            {
                var direct = Path.Combine(AppContext.BaseDirectory, "Resources", "banner", fileName);
                if (File.Exists(direct)) return direct;

                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                {
                    var a = Path.Combine(dir.FullName, "Resources", "banner", fileName);
                    if (File.Exists(a)) return a;
                    var b = Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources", "banner", fileName);
                    if (File.Exists(b)) return b;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[BannerPool] pool probe failed: {E}", ex.Message);
            }
            return null;
        }
    }
}
