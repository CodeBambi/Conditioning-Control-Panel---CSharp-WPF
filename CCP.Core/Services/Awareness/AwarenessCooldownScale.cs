using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The Awareness cooldown sliders move along a ladder of stops, not raw seconds
    /// (ccp-bugs #640). The range is 1 s to an hour, and a straight 1-3600 slider would
    /// squeeze everything under a minute into a few pixels. The slider value is the stop
    /// INDEX; the setting keeps plain seconds, so a stored value off the ladder (an old
    /// settings file, a preset) still loads and is shown as it is.
    /// </summary>
    public static class AwarenessCooldownScale
    {
        public const int MinSeconds = 1;
        public const int MaxSeconds = 3600;

        /// <summary>Every 1 s to 30 s, every 5 s to 2 min, every 30 s to 10 min, every 5 min to an hour.</summary>
        public static readonly IReadOnlyList<int> Stops = BuildStops();

        public static int MaxIndex => Stops.Count - 1;

        private static int[] BuildStops()
        {
            var list = new List<int>();
            for (int s = 1; s <= 30; s++) list.Add(s);
            for (int s = 35; s <= 120; s += 5) list.Add(s);
            for (int s = 150; s <= 600; s += 30) list.Add(s);
            for (int s = 900; s <= MaxSeconds; s += 300) list.Add(s);
            return list.ToArray();
        }

        public static int Clamp(int seconds) => Math.Clamp(seconds, MinSeconds, MaxSeconds);

        /// <summary>Seconds for a slider position (rounded and clamped to the ladder).</summary>
        public static int SecondsAt(double index)
        {
            var i = (int)Math.Round(index);
            return Stops[Math.Clamp(i, 0, MaxIndex)];
        }

        /// <summary>The slider position nearest to a stored number of seconds.</summary>
        public static int IndexFor(int seconds)
        {
            var target = Clamp(seconds);
            int best = 0;
            int bestGap = int.MaxValue;
            for (int i = 0; i < Stops.Count; i++)
            {
                var gap = Math.Abs(Stops[i] - target);
                if (gap < bestGap) { best = i; bestGap = gap; }
            }
            return best;
        }

        /// <summary>"45s" under a minute, "m:ss" from a minute up ("1:30", "60:00").</summary>
        public static string Format(int seconds)
        {
            var s = Clamp(seconds);
            if (s < 60) return $"{s}s";
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
