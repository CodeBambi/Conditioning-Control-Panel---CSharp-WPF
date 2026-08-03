using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// PHASE F. Pure, dependency-free .funscript parsing + sampling.
    ///
    /// Deliberately split out of <see cref="FunScriptService"/>: this file references NOTHING from
    /// the app (no App, no WPF, no settings), so the parser and the stroke→vibration conversion can
    /// be compiled and asserted standalone. <see cref="FunScriptService"/> owns all the wiring.
    ///
    /// Format (de-facto standard): <c>{"actions":[{"at":&lt;ms&gt;,"pos":&lt;0-100&gt;}, ...]}</c>.
    /// Everything else in the file (metadata, "range", "version", "chapters", …) is ignored, except
    /// <c>"inverted"</c>, which is honoured because it changes what the numbers MEAN.
    /// </summary>
    public sealed class FunScript
    {
        /// <summary>Sorted by <see cref="FunScriptAction.AtMs"/>, ascending. Never null, never empty
        /// for an instance returned by <see cref="TryParse"/>.</summary>
        public IReadOnlyList<FunScriptAction> Actions { get; }

        /// <summary>Timestamp of the last action; the script is over after this.</summary>
        public int DurationMs => Actions.Count == 0 ? 0 : Actions[Actions.Count - 1].AtMs;

        private FunScript(IReadOnlyList<FunScriptAction> actions) { Actions = actions; }

        // ------------------------------------------------------------------ vibe conversion

        /// <summary>Below this stroke speed (position units per second) a vibe-only toy stays
        /// silent — slow drift is not a stroke.</summary>
        public const double MinSpeedUnitsPerSec = 10.0;
        /// <summary>At or above this stroke speed the envelope is at full scale. 500 units/s is
        /// about as fast as human-authored scripts get (a full 0→100 stroke in 200 ms).</summary>
        public const double MaxSpeedUnitsPerSec = 500.0;

        /// <summary>
        /// Parse a .funscript document. Returns false (and null) for anything without at least one
        /// usable action — a broken or foreign JSON file is simply "no script", never an exception.
        /// </summary>
        public static bool TryParse(string json, out FunScript? script)
        {
            script = null;
            if (string.IsNullOrWhiteSpace(json)) return false;

            try
            {
                var root = JsonConvert.DeserializeObject<JObject>(json);
                var rawActions = root?["actions"] as JArray;
                if (rawActions == null || rawActions.Count == 0) return false;

                bool inverted = FunScriptJsonExtensions.IsTrue(root?["inverted"]);

                var list = new List<FunScriptAction>(rawActions.Count);
                foreach (var token in rawActions)
                {
                    if (token is not JObject o) continue;
                    var at = o["at"];
                    var pos = o["pos"];
                    if (at == null || pos == null) continue;

                    double atMs, posValue;
                    try
                    {
                        atMs = at.Value<double>();
                        posValue = pos.Value<double>();
                    }
                    catch { continue; }

                    if (double.IsNaN(atMs) || double.IsNaN(posValue) || atMs < 0) continue;
                    var p = Math.Clamp(posValue, 0, 100);
                    if (inverted) p = 100 - p;
                    list.Add(new FunScriptAction((int)Math.Round(atMs), (int)Math.Round(p)));
                }

                if (list.Count == 0) return false;

                list.Sort(static (a, b) => a.AtMs.CompareTo(b.AtMs));
                script = new FunScript(list);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ sampling

        /// <summary>
        /// Absolute stroke position at <paramref name="timeMs"/>, 0..1, linearly interpolated
        /// between the surrounding actions. Before the first action it holds the first position;
        /// after the last it holds the last one.
        /// </summary>
        public double PositionAt(double timeMs)
        {
            var a = Actions;
            if (a.Count == 0) return 0;
            if (timeMs <= a[0].AtMs) return a[0].Pos / 100.0;
            var last = a[a.Count - 1];
            if (timeMs >= last.AtMs) return last.Pos / 100.0;

            var i = UpperBound(timeMs);          // first index with AtMs > timeMs
            var prev = a[i - 1];
            var next = a[i];
            var span = next.AtMs - prev.AtMs;
            if (span <= 0) return next.Pos / 100.0;
            var t = (timeMs - prev.AtMs) / span;
            return Math.Clamp((prev.Pos + (next.Pos - prev.Pos) * t) / 100.0, 0, 1);
        }

        /// <summary>Speed of the stroke in progress at <paramref name="timeMs"/>, in position
        /// units (0-100) per second. Zero outside the scripted range.</summary>
        public double SpeedAt(double timeMs)
        {
            var a = Actions;
            if (a.Count < 2) return 0;
            if (timeMs < a[0].AtMs || timeMs > a[a.Count - 1].AtMs) return 0;

            var i = UpperBound(timeMs);
            if (i <= 0 || i >= a.Count) return 0;
            var prev = a[i - 1];
            var next = a[i];
            var spanMs = next.AtMs - prev.AtMs;
            if (spanMs <= 0) return 0;
            return Math.Abs(next.Pos - prev.Pos) * 1000.0 / spanMs;
        }

        /// <summary>
        /// Vibration intensity 0..1 for a toy with no Position actuator: the standard
        /// funscript→vibe conversion, i.e. FASTER STROKES = STRONGER. Linear between
        /// <see cref="MinSpeedUnitsPerSec"/> and <see cref="MaxSpeedUnitsPerSec"/>.
        /// </summary>
        public double IntensityAt(double timeMs) => SpeedToIntensity(SpeedAt(timeMs));

        public static double SpeedToIntensity(double unitsPerSec)
        {
            if (unitsPerSec <= MinSpeedUnitsPerSec) return 0;
            if (unitsPerSec >= MaxSpeedUnitsPerSec) return 1;
            return (unitsPerSec - MinSpeedUnitsPerSec) / (MaxSpeedUnitsPerSec - MinSpeedUnitsPerSec);
        }

        /// <summary>Index of the first action strictly after <paramref name="timeMs"/>.
        /// Callers guarantee 0 &lt; result &lt; Count by range-checking first.</summary>
        private int UpperBound(double timeMs)
        {
            var a = Actions;
            int lo = 0, hi = a.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (a[mid].AtMs > timeMs) hi = mid;
                else lo = mid + 1;
            }
            return lo;
        }
    }

    /// <summary>One keyframe of a .funscript: be at <see cref="Pos"/> (0-100) at <see cref="AtMs"/>.</summary>
    public readonly struct FunScriptAction
    {
        public FunScriptAction(int atMs, int pos) { AtMs = atMs; Pos = pos; }
        public int AtMs { get; }
        public int Pos { get; }
    }

    internal static class FunScriptJsonExtensions
    {
        /// <summary>Truthiness for a token that may be absent, a bool, a number or a string.</summary>
        public static bool IsTrue(this JToken? token)
        {
            if (token == null) return false;
            try
            {
                return token.Type switch
                {
                    JTokenType.Boolean => token.Value<bool>(),
                    JTokenType.Integer or JTokenType.Float => token.Value<double>() != 0,
                    JTokenType.String => bool.TryParse(token.Value<string>(), out var b) && b,
                    _ => false
                };
            }
            catch { return false; }
        }
    }
}
