using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Views.Deeper
{
    // Pure, Window-free helpers for the Deeper editor timeline so the lane
    // math, colour picking and time formatting can be unit-tested without
    // spinning up WPF. The editor partials import this with `using static`.
    internal static class DeeperEditorGeometry
    {
        // The timeline canvas is split into three equal horizontal lanes, top to
        // bottom: Regions, Effects, Haptics. Rules are NOT a lane - they render as
        // full-height pins layered across all three. This is the single source of
        // truth for lane geometry; every Build*/hit-test path resolves its Y band
        // through LaneBand().
        internal enum TimelineLane { Regions, Effects, Haptics }

        internal static (double top, double height) LaneBand(TimelineLane lane, double canvasHeight)
        {
            if (canvasHeight <= 0) return (0, 0);
            double laneH = canvasHeight / 3.0;
            double top = lane switch
            {
                TimelineLane.Regions => 0,
                TimelineLane.Effects => laneH,
                TimelineLane.Haptics => 2 * laneH,
                _ => 0
            };
            return (top, laneH);
        }

        // Inset band rect for a lane (a few px of breathing room above/below so
        // adjacent lanes read as distinct). Used by region/haptic/effect-segment bands.
        internal const double LaneInset = 2.0;
        internal static (double top, double height) LaneBandInset(TimelineLane lane, double canvasHeight)
        {
            var (top, height) = LaneBand(lane, canvasHeight);
            return (top + LaneInset, Math.Max(0, height - 2 * LaneInset));
        }

        /// <summary>Transport readout: m:ss.f (h:mm:ss.f past an hour), tenths floored
        /// so the label never shows a second the playhead has not reached.</summary>
        internal static string FormatTransportTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            long tenths = (long)Math.Floor(seconds * 10 + 1e-6);
            long s = tenths / 10, t = tenths % 10;
            long h = s / 3600, m = (s % 3600) / 60, sec = s % 60;
            return h > 0 ? $"{h}:{m:D2}:{sec:D2}.{t}" : $"{m}:{sec:D2}.{t}";
        }

        /// <summary>Hover readout over the ruler / canvas: m:ss.ff.</summary>
        internal static string FormatHoverTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            long hund = (long)Math.Floor(seconds * 100 + 1e-6);
            long s = hund / 100, c = hund % 100;
            long h = s / 3600, m = (s % 3600) / 60, sec = s % 60;
            return h > 0 ? $"{h}:{m:D2}:{sec:D2}.{c:D2}" : $"{m}:{sec:D2}.{c:D2}";
        }

        internal static bool RangesOverlap(double a1, double a2, double b1, double b2) => a1 < b2 && a2 > b1;

        /// <summary>True when a rubber-band spanning [yMin, yMax] touches the given lane.</summary>
        internal static bool BandHitsLane(TimelineLane lane, double yMin, double yMax, double canvasHeight)
        {
            var (top, height) = LaneBand(lane, canvasHeight);
            if (height <= 0) return false;
            return RangesOverlap(yMin, yMax, top, top + height);
        }

        // -- Rule pins -------------------------------------------------------
        // Rules are the one item kind with no lane of their own. A time rule is a
        // pin at its time; a band rule reads as its region. ccp-bugs #1245: the two
        // rules that are neither (a time past the end of the media, and a band rule
        // whose region was deleted or set to none) used to draw nothing at all - the
        // first off the clipped canvas, the second never even considered - so the
        // enhancement carried a rule the timeline said was not there.

        internal enum RulePinKind
        {
            /// <summary>Nothing to draw. A time rule against a media length we do not have
            /// yet: its x would be a guess, and a lane full of guesses on every open is
            /// worse than the rule waiting for the duration to land.</summary>
            None,
            /// <summary>At its trigger time, inside the media.</summary>
            AtTime,
            /// <summary>A time rule past the end of the media, parked near the right edge.</summary>
            PastEnd,
            /// <summary>A band rule: its region draws it, so the pin lane has nothing to add.</summary>
            OnItsBand,
            /// <summary>No usable time and no band. Parked in the left gutter, in order.</summary>
            Detached,
        }

        /// <summary>True for the two kinds whose x is a parking spot rather than a time.</summary>
        internal static bool IsStrayPin(RulePinKind kind)
            => kind is RulePinKind.PastEnd or RulePinKind.Detached;

        /// <summary>Half the pin's hit rect: a parked pin has to stay fully on a canvas
        /// that clips.</summary>
        internal const double RulePinEdgeInset = 7.0;
        /// <summary>Width of a pin's click target, in sync with BuildRulePin's Rectangle.</summary>
        internal const double RulePinHitWidth = 14.0;
        /// <summary>Gap between parked markers so several of them stay separately clickable.</summary>
        internal const double DetachedRulePinSpacing = 16.0;
        /// <summary>A rule sitting exactly on the last frame is not "past the end".</summary>
        private const double RulePinEndEpsilon = 1e-6;

        /// <summary>
        /// What the pin lane should do with one rule. <paramref name="triggerTime"/> is null
        /// for every trigger that is not time-reached; <paramref name="hasRegionBand"/> is
        /// whether its region constraint still resolves to a region on the timeline.
        /// </summary>
        internal static RulePinKind ClassifyRulePin(double? triggerTime, bool hasRegionBand, double totalSeconds)
        {
            if (triggerTime is { } t && double.IsFinite(t))
            {
                // No media length means the ruler means nothing: the file has not loaded (audio
                // takes its length from the waveform), or it has moved. A time rule then has no
                // honest x at all, and a comb of markers on every editor open would say the
                // enhancement is something it is not. It comes back when the duration does.
                if (!(totalSeconds > 0) || !double.IsFinite(totalSeconds)) return RulePinKind.None;
                return t > totalSeconds + RulePinEndEpsilon ? RulePinKind.PastEnd : RulePinKind.AtTime;
            }
            // No usable time - including the NaN a hand-edited file can carry in, which
            // TryParseDouble and Newtonsoft both accept. Its band draws it, or it is stray.
            return hasRegionBand ? RulePinKind.OnItsBand : RulePinKind.Detached;
        }

        /// <summary>
        /// X for a rule pin. An <see cref="RulePinKind.AtTime"/> pin keeps its TRUE x, because
        /// the line is a claim about a time and must not lie by a few pixels; only its click
        /// target is nudged inboard, by <see cref="RulePinHitLeft"/>. The two parked kinds walk
        /// in from their own edge by <paramref name="strayIndex"/> - each kind counts its own -
        /// so a run of them stays separately clickable, and stack at the far edge rather than
        /// leaving the canvas.
        /// </summary>
        internal static double RulePinX(RulePinKind kind, double triggerTime, double totalSeconds,
                                        double canvasWidth, int strayIndex)
        {
            if (!(canvasWidth > 0)) return 0;
            double lo = Math.Min(RulePinEdgeInset, canvasWidth / 2);
            double hi = Math.Max(lo, canvasWidth - RulePinEdgeInset);
            double step = Math.Max(0, strayIndex) * DetachedRulePinSpacing;
            return kind switch
            {
                RulePinKind.PastEnd  => Math.Max(lo, hi - step),
                RulePinKind.Detached => Math.Min(hi, lo + step),
                RulePinKind.AtTime   => totalSeconds > 0
                    ? Math.Max(0, triggerTime) / totalSeconds * canvasWidth
                    : 0,
                _ => 0,
            };
        }

        /// <summary>Left edge of a pin's click target: centred on the pin, but pulled back
        /// onto a canvas that clips, so a pin at 0 s or at the last frame is still clickable
        /// without its line moving.</summary>
        internal static double RulePinHitLeft(double x, double canvasWidth)
            => Math.Clamp(x - RulePinEdgeInset, 0, Math.Max(0, canvasWidth - RulePinHitWidth));

        /// <summary>
        /// Picks the palette entry used by the fewest existing items (first wins on
        /// ties) so neighbours stop repeating after deletes shift the count.
        /// </summary>
        internal static string LeastUsedPaletteColor(IReadOnlyList<string> palette, IEnumerable<string?> used)
        {
            if (palette.Count == 0) return "#7B5CFF";
            var counts = new int[palette.Count];
            foreach (var u in used)
            {
                if (string.IsNullOrEmpty(u)) continue;
                for (int i = 0; i < palette.Count; i++)
                {
                    if (string.Equals(palette[i], u, StringComparison.OrdinalIgnoreCase)) { counts[i]++; break; }
                }
            }
            int best = 0;
            for (int i = 1; i < palette.Count; i++)
                if (counts[i] < counts[best]) best = i;
            return palette[best];
        }
    }
}
