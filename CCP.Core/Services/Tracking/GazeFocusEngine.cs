using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>Video = a mandatory video's attention target (WPF IAttentionTarget, "Floating"): the head only
    /// lists them while VideoGazeClickEnabled is on, a dwell clicks one, a blink does not.</summary>
    public enum GazeTargetKind { Flash, Bubble, Video }

    /// <summary>One thing a gaze dwell can land on. Bounds are in the same space as the gaze point the
    /// head feeds the engine (width 0 = not hittable this tick).</summary>
    public interface IGazeTarget
    {
        GazeTargetKind Kind { get; }
        /// <summary>Stable identity across ticks (the bubble or the flash window itself).</summary>
        object Key { get; }
        (double X, double Y, double W, double H) Bounds { get; }
        /// <summary>0..1 dwell fill; 0 clears it.</summary>
        void SetDwellProgress(double t01);
        /// <summary>The dwell (or a blink over it) completed: pop it through its own click pipeline.</summary>
        void Activate();
        /// <summary>Stare-linger: keep it alive this much longer from now.</summary>
        void BoostLifetime(int extraMs);
    }

    /// <summary>What the engine may do this tick, re-read from settings by the head every tick.</summary>
    public readonly record struct GazeFocusOptions(bool Bubbles, bool FlashPop, bool FlashLinger, int LingerExtensionMs);

    /// <summary>
    /// The rules of WPF GazeFocusService (Services/Tracking/GazeFocusService.cs, 7.1.5), without a window
    /// or a camera: predictive target scoring (Gaussian falloff from the rect edge, a sticky bonus for the
    /// target already dwelt on, flashes outrank bubbles), the 600 ms dwell, the 250 ms cooldown, the
    /// stare-linger boost every 250 ms and the blink-to-pop shortcut. The head owns the clock, the gaze
    /// stream and the target list.
    /// not ported: the two-stage zoom refine (GazeRefineOverlay), the cursor lock-on and video attention targets.
    /// </summary>
    public sealed class GazeFocusEngine
    {
        public const int DefaultDwellMs = 600;
        public const int CooldownMs = 250;
        public const int TickMs = 33;
        public const int LingerBoostThrottleMs = 250;
        public const double BubbleScoreSigma = 160;
        public const double FlashScoreSigma = 90;
        public const double StickyBonus = 0.20;
        public const double FlashTypeBonus = 0.15;
        public const double ScoreThreshold = 0.05;

        public int DwellMs { get; set; } = DefaultDwellMs;

        private (double X, double Y)? _gaze;
        private bool _faceLost;
        private IGazeTarget? _current;
        private DateTime _dwellStartedAt;
        private DateTime _cooldownUntil = DateTime.MinValue;
        private DateTime _lastLingerBoostAt = DateTime.MinValue;

        /// <summary>The target being dwelt on, for the head's cursor / tests.</summary>
        public IGazeTarget? Current => _current;
        /// <summary>A bubble was popped by gaze (WPF GazePopped).</summary>
        public event Action? GazePopped;

        public void GazeMoved(double x, double y) => _gaze = (x, y);
        public void FaceLost() => _faceLost = true;
        public void FaceFound() => _faceLost = false;

        /// <summary>WPF Stop: forget everything, clear the dwell fill.</summary>
        public void Reset()
        {
            ClearTarget();
            _gaze = null;
            _faceLost = false;
            _cooldownUntil = DateTime.MinValue;
        }

        public static double GaussianScore(double dist, double sigma)
        {
            var d = dist / sigma;
            return Math.Exp(-0.5 * d * d);
        }

        public static double DistanceFromRectEdge((double X, double Y, double W, double H) r, double px, double py)
        {
            var dx = Math.Max(0, Math.Max(r.X - px, px - (r.X + r.W)));
            var dy = Math.Max(0, Math.Max(r.Y - py, py - (r.Y + r.H)));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>WPF FindBestTarget: the single highest score above the threshold; flashes first so a
        /// tie goes to the foreground picture.</summary>
        public IGazeTarget? FindBestTarget(double px, double py, IReadOnlyList<IGazeTarget> targets, GazeFocusOptions o)
        {
            IGazeTarget? best = null;
            double bestScore = ScoreThreshold;
            // Third pass: video attention targets, scored like bubbles with no type bonus, so a flash still
            // wins when both are looked at (WPF GazeFocusService :1005).
            for (int pass = 0; pass < 3; pass++)
            {
                var kind = pass == 0 ? GazeTargetKind.Flash : pass == 1 ? GazeTargetKind.Bubble : GazeTargetKind.Video;
                if (kind == GazeTargetKind.Bubble && !o.Bubbles) continue;
                if (kind == GazeTargetKind.Flash && !o.FlashPop && !o.FlashLinger) continue;
                for (int i = targets.Count - 1; i >= 0; i--)
                {
                    var t = targets[i];
                    if (t.Kind != kind) continue;
                    var r = t.Bounds;
                    if (r.W <= 0 || r.H <= 0) continue;
                    var dist = DistanceFromRectEdge(r, px, py);
                    var score = kind == GazeTargetKind.Flash
                        ? GaussianScore(dist, FlashScoreSigma) + FlashTypeBonus
                        : GaussianScore(dist, BubbleScoreSigma);
                    if (_current != null && ReferenceEquals(_current.Key, t.Key)) score += StickyBonus;
                    if (score > bestScore) { bestScore = score; best = t; }
                }
            }
            return best;
        }

        /// <summary>WPF OnTick. Returns the target acquired this tick (null = none).</summary>
        public IGazeTarget? Tick(DateTime now, IReadOnlyList<IGazeTarget> targets, GazeFocusOptions o)
        {
            if (now < _cooldownUntil || _faceLost || _gaze is not { } g) { ClearTarget(); return null; }
            var hit = FindBestTarget(g.X, g.Y, targets, o);
            if (hit == null) { ClearTarget(); return null; }

            if (hit.Kind == GazeTargetKind.Bubble && !o.Bubbles) { ClearTarget(); return null; }
            if (_current == null || !ReferenceEquals(_current.Key, hit.Key))
            {
                ClearTarget();
                _dwellStartedAt = now;
                _lastLingerBoostAt = DateTime.MinValue;
            }
            _current = hit;   // the adapter is rebuilt every tick; the key is what persists
            var elapsedMs = (now - _dwellStartedAt).TotalMilliseconds;

            if (hit.Kind == GazeTargetKind.Video)
            {
                // WPF AdvanceFloatingTextDwell: the dwell clicks the target (the same Hit as a mouse click).
                if (elapsedMs >= DwellMs)
                {
                    Safe(hit.Activate);
                    _current = null;
                    _cooldownUntil = now.AddMilliseconds(CooldownMs);
                }
                return hit;
            }

            if (hit.Kind == GazeTargetKind.Bubble)
            {
                Safe(() => hit.SetDwellProgress(elapsedMs / DwellMs));
                if (elapsedMs >= DwellMs)
                {
                    Safe(hit.Activate);
                    Safe(() => GazePopped?.Invoke());
                    _current = null;
                    _cooldownUntil = now.AddMilliseconds(CooldownMs);
                }
                return hit;
            }

            if (o.FlashPop) Safe(() => hit.SetDwellProgress(elapsedMs / DwellMs));
            if (o.FlashLinger && (now - _lastLingerBoostAt).TotalMilliseconds >= LingerBoostThrottleMs)
            {
                Safe(() => hit.BoostLifetime(o.LingerExtensionMs));
                _lastLingerBoostAt = now;
            }
            if (o.FlashPop && elapsedMs >= DwellMs)
            {
                Safe(hit.Activate);
                _current = null;
                _cooldownUntil = now.AddMilliseconds(CooldownMs);
            }
            return hit;
        }

        /// <summary>WPF HandleBlink: a blink while looking near a target pops it at once.</summary>
        public bool Blink(DateTime now, IReadOnlyList<IGazeTarget> targets, GazeFocusOptions o)
        {
            if (now < _cooldownUntil || _faceLost || _gaze is not { } g) return false;
            var hit = FindBestTarget(g.X, g.Y, targets, o);
            if (hit == null) return false;
            ClearTarget();
            bool fired = false;
            if (hit.Kind == GazeTargetKind.Video) { }   // a blink never clicks an attention target
            else if (hit.Kind == GazeTargetKind.Bubble)
            {
                if (o.Bubbles) { Safe(hit.Activate); Safe(() => GazePopped?.Invoke()); fired = true; }
            }
            else if (o.FlashPop) { Safe(hit.Activate); fired = true; }
            _cooldownUntil = now.AddMilliseconds(CooldownMs);
            return fired;
        }

        private void ClearTarget()
        {
            if (_current != null) { var c = _current; Safe(() => c.SetDwellProgress(0)); _current = null; }
            _lastLingerBoostAt = DateTime.MinValue;
        }

        private static void Safe(Action a)
        {
            try { a(); } catch (Exception ex) { Serilog.Log.Debug("GazeFocus: target call failed: {Error}", ex.Message); }
        }
    }
}
