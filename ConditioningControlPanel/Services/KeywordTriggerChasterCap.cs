using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Per-trigger ceiling on the Chaster time an Awareness keyword trigger may book. A preset (or
    /// a pasted trigger file) names its own minutes, and OCR can see the same word all day, so each
    /// trigger gets its own small allowance on top of the tab's daily limit:
    /// at most <see cref="MaxFiresPerDay"/> bookings and <see cref="MaxSecondsPerDay"/> seconds per
    /// trigger id per LOCAL day. Past either, the Chaster action books nothing; the trigger's other
    /// actions still run.
    ///
    /// <para>Pure: no WPF, no <c>App.*</c>. The clock comes in as an argument. State is a plain
    /// dictionary keyed by trigger id so it can be saved to disk and survive a restart.</para>
    /// </summary>
    public sealed class KeywordTriggerChasterCap
    {
        public const int MaxFiresPerDay = 3;
        public const int MaxSecondsPerDay = 15 * 60;

        /// <summary>One trigger's usage for one local day.</summary>
        public sealed class Usage
        {
            [JsonProperty("day")] public string Day { get; set; } = "";
            [JsonProperty("fires")] public int Fires { get; set; }
            [JsonProperty("seconds")] public int Seconds { get; set; }
        }

        private readonly Dictionary<string, Usage> _byTrigger;

        public KeywordTriggerChasterCap() : this(null) { }

        public KeywordTriggerChasterCap(Dictionary<string, Usage>? state)
        {
            _byTrigger = state ?? new Dictionary<string, Usage>(StringComparer.Ordinal);
        }

        /// <summary>The raw state, for saving.</summary>
        public IReadOnlyDictionary<string, Usage> State => _byTrigger;

        public static string DayKey(DateTime localNow) => localNow.ToString("yyyy-MM-dd");

        /// <summary>
        /// How many of <paramref name="requestedSeconds"/> this trigger may still book today.
        /// Zero once the trigger has booked <see cref="MaxFiresPerDay"/> times or spent
        /// <see cref="MaxSecondsPerDay"/>. Does not record anything: call <see cref="Record"/> with
        /// what actually landed on the tab.
        /// </summary>
        public int Allowance(string triggerId, int requestedSeconds, DateTime localNow)
        {
            if (string.IsNullOrEmpty(triggerId) || requestedSeconds <= 0) return 0;
            var u = Today(triggerId, localNow);
            if (u == null) return Math.Min(requestedSeconds, MaxSecondsPerDay);
            if (u.Fires >= MaxFiresPerDay) return 0;
            var left = MaxSecondsPerDay - u.Seconds;
            return left <= 0 ? 0 : Math.Min(requestedSeconds, left);
        }

        /// <summary>Counts one booking of <paramref name="bookedSeconds"/>. A booking that landed
        /// nothing (tab off, no link, safety hold) is not counted.</summary>
        public void Record(string triggerId, int bookedSeconds, DateTime localNow)
        {
            if (string.IsNullOrEmpty(triggerId) || bookedSeconds <= 0) return;
            var day = DayKey(localNow);
            var u = Today(triggerId, localNow);
            if (u == null)
            {
                u = new Usage { Day = day };
                _byTrigger[triggerId] = u;
            }
            u.Fires++;
            u.Seconds = Math.Min(MaxSecondsPerDay, u.Seconds + bookedSeconds);
            Prune(day);
        }

        private Usage? Today(string triggerId, DateTime localNow)
        {
            if (!_byTrigger.TryGetValue(triggerId, out var u) || u == null) return null;
            return u.Day == DayKey(localNow) ? u : null;
        }

        // Only today's rows matter; drop the rest so the file never grows.
        private void Prune(string today)
        {
            foreach (var key in _byTrigger.Where(kv => kv.Value?.Day != today).Select(kv => kv.Key).ToList())
                _byTrigger.Remove(key);
        }

        // ---- persistence --------------------------------------------------------

        public static KeywordTriggerChasterCap Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var state = JsonConvert.DeserializeObject<Dictionary<string, Usage>>(File.ReadAllText(path));
                    if (state != null)
                    {
                        // Sanitise a hand-edited file: never trust negative or oversized counts.
                        foreach (var u in state.Values.Where(v => v != null))
                        {
                            u.Fires = Math.Clamp(u.Fires, 0, MaxFiresPerDay);
                            u.Seconds = Math.Clamp(u.Seconds, 0, MaxSecondsPerDay);
                        }
                        return new KeywordTriggerChasterCap(
                            new Dictionary<string, Usage>(state.Where(kv => kv.Value != null), StringComparer.Ordinal));
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("KeywordTriggerChasterCap: could not read {Path}: {Error}", path, ex.Message);
            }
            return new KeywordTriggerChasterCap();
        }

        public void Save(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_byTrigger));
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("KeywordTriggerChasterCap: could not write {Path}: {Error}", path, ex.Message);
            }
        }
    }

    /// <summary>
    /// What an incoming set of triggers (a preset being activated, a trigger file) would put on a
    /// Chaster lock, and the switch that makes those actions arrive OFF. Pure.
    /// </summary>
    public static class KeywordTriggerChasterImport
    {
        /// <summary>How many enabled Chaster add-time actions the triggers carry, and the minutes
        /// they would add if every one fired once (each counted at most the per-trigger day cap,
        /// since one fire can never book more than that).</summary>
        public readonly record struct Summary(int Count, int MinutesPerFire)
        {
            public bool Any => Count > 0;
        }

        public static Summary Summarise(IEnumerable<KeywordTrigger?>? triggers)
        {
            int count = 0, minutes = 0;
            foreach (var a in ChasterActions(triggers))
            {
                if (!a.Enabled) continue;
                count++;
                minutes += Math.Clamp(a.Minutes, 0, KeywordTriggerChasterCap.MaxSecondsPerDay / 60);
            }
            return new Summary(count, minutes);
        }

        /// <summary>Switches every Chaster add-time action off. Returns how many it switched.</summary>
        public static int DisableAll(IEnumerable<KeywordTrigger?>? triggers)
        {
            int n = 0;
            foreach (var a in ChasterActions(triggers))
            {
                if (!a.Enabled) continue;
                a.Enabled = false;
                n++;
            }
            return n;
        }

        /// <summary>True when any of the triggers has a Chaster add-time action switched on.</summary>
        public static bool AnyEnabled(IEnumerable<KeywordTrigger?>? triggers)
            => ChasterActions(triggers).Any(a => a.Enabled);

        private static IEnumerable<ChasterAddTimeAction> ChasterActions(IEnumerable<KeywordTrigger?>? triggers)
        {
            if (triggers == null) yield break;
            foreach (var t in triggers)
            {
                if (t?.Actions == null) continue;
                foreach (var a in t.Actions.OfType<ChasterAddTimeAction>())
                    yield return a;
            }
        }
    }
}
