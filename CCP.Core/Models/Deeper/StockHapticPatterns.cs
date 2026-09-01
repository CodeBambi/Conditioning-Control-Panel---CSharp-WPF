using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Models.Deeper
{
    /// <summary>
    /// Named haptic patterns available to creators in the Deeper editor.
    /// Each is a list of [t_frac, intensity] keyframes in [0, 1]; sampled
    /// to a flat float[] at dispatch time via HapticService.SetSyncPatternAsync.
    /// </summary>
    public static class StockHapticPatterns
    {
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "Pulse", "Throb", "Wave", "Steady", "Climax", "Tease"
        };

        private static readonly Dictionary<string, double[][]> _patterns = new()
        {
            ["Pulse"]  = new[] { new[] { 0.0, 0.0 }, new[] { 0.5, 1.0 }, new[] { 1.0, 0.0 } },
            ["Throb"]  = new[] { new[] { 0.0, 0.3 }, new[] { 0.2, 1.0 }, new[] { 0.5, 0.5 }, new[] { 0.8, 1.0 }, new[] { 1.0, 0.3 } },
            ["Wave"]   = new[] { new[] { 0.0, 0.0 }, new[] { 0.25, 1.0 }, new[] { 0.5, 0.0 }, new[] { 0.75, 1.0 }, new[] { 1.0, 0.0 } },
            ["Steady"] = new[] { new[] { 0.0, 1.0 }, new[] { 1.0, 1.0 } },
            ["Climax"] = new[] { new[] { 0.0, 0.0 }, new[] { 0.5, 0.4 }, new[] { 0.8, 0.7 }, new[] { 1.0, 1.0 } },
            ["Tease"]  = new[] { new[] { 0.0, 0.0 }, new[] { 0.2, 1.0 }, new[] { 0.4, 0.0 }, new[] { 0.6, 1.0 }, new[] { 0.8, 0.0 }, new[] { 1.0, 0.5 } },
        };

        public static bool TryGet(string name, out double[][]? keyframes)
        {
            if (!string.IsNullOrEmpty(name) && _patterns.TryGetValue(name, out var kf))
            {
                keyframes = kf;
                return true;
            }
            keyframes = null;
            return false;
        }

        /// <summary>
        /// Samples a pattern (stock or custom keyframes) into an evenly-spaced float[]
        /// scaled by intensity. N is clamped to [8, 64] based on duration.
        /// </summary>
        public static float[] Sample(IList<double[]> keyframes, double intensity, int durationMs)
        {
            int n = Math.Clamp(durationMs / 50, 8, 64);
            var result = new float[n];
            if (keyframes == null || keyframes.Count == 0)
                return result;

            var scale = (float)Math.Clamp(intensity, 0.0, 1.0);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Math.Max(1, n - 1);
                double v = InterpAt(keyframes, t);
                result[i] = (float)Math.Clamp(v * scale, 0.0, 1.0);
            }
            return result;
        }

        private static double InterpAt(IList<double[]> kf, double t)
        {
            if (kf.Count == 0) return 0;
            if (t <= kf[0][0]) return kf[0][1];
            if (t >= kf[^1][0]) return kf[^1][1];

            for (int i = 1; i < kf.Count; i++)
            {
                var a = kf[i - 1];
                var b = kf[i];
                if (a == null || b == null || a.Length < 2 || b.Length < 2) continue;
                if (t >= a[0] && t <= b[0])
                {
                    double span = b[0] - a[0];
                    if (span <= 0) return b[1];
                    double f = (t - a[0]) / span;
                    return a[1] + (b[1] - a[1]) * f;
                }
            }
            return kf[^1][1];
        }

        /// <summary>
        /// Initial keyframes for the "Custom..." mode — 5 evenly-spaced points
        /// shaped to roughly match the named pattern (or flat 0.5 if unknown).
        /// </summary>
        public static List<double[]> SeedCustomFrom(string? patternName)
        {
            double[] xs = { 0.0, 0.25, 0.5, 0.75, 1.0 };
            var seed = new List<double[]>(xs.Length);
            if (TryGet(patternName ?? "", out var kf) && kf != null)
            {
                foreach (var x in xs)
                    seed.Add(new[] { x, Math.Clamp(InterpAt(kf, x), 0.0, 1.0) });
            }
            else
            {
                foreach (var x in xs)
                    seed.Add(new[] { x, 0.5 });
            }
            return seed;
        }
    }
}
