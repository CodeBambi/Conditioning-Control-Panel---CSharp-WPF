using System;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>How much motion Lights Down may use. Mirrors MotionLevel without the WPF/App reach.</summary>
    public enum LightsDownMotion { Full, Reduced, Off }

    /// <summary>
    /// One frame of Lights Down, everything the overlay window needs to draw. All values are
    /// already scaled by depth, motion level and photosafe, so the window only copies them onto
    /// its visuals. Angles in radians, hue in degrees.
    /// </summary>
    public struct LightsDownFrame
    {
        /// <summary>Depth after the stutter, 0..0.92. 0 means draw nothing.</summary>
        public double Depth;
        /// <summary>Alpha of the dim over the room (outside the picture), heartbeat included.</summary>
        public double DimAlpha;
        /// <summary>The heartbeat share of <see cref="DimAlpha"/>, 0..0.05.</summary>
        public double Beat;
        /// <summary>0..1 how closed the aperture is (eased depth). Radius comes from <see cref="LightsDownMath.ApertureRadius"/>.</summary>
        public double ApertureClose;
        /// <summary>Alpha of EACH of the four aperture layers.</summary>
        public double ApertureAlpha;
        /// <summary>Aperture rotation, integrated so a depth change never makes it jump.</summary>
        public double ApertureRotation;
        /// <summary>Light hue, 300 +- 40 degrees.</summary>
        public double Hue;
        /// <summary>Rotation of the ray fan.</summary>
        public double RayAngle;
        /// <summary>Peak alpha of the rays (each ray adds its own shimmer, see <see cref="LightsDownMath.RayAlpha"/>).</summary>
        public double RayAlpha;
        /// <summary>Scale of the video and its glow, 1..1.07.</summary>
        public double Swell;
        /// <summary>Glow around the frame: alpha and blur radius in DIPs.</summary>
        public double GlowAlpha;
        public double GlowBlur;
        /// <summary>Red/cyan split over the picture on a stutter, 0 when none.</summary>
        public double SplitAlpha;
        /// <summary>The lens streak across the picture on a stutter, 0 when none.</summary>
        public double StreakAlpha;
        /// <summary>Progress 0..1 of the lock-in light run around the frame, or -1 when none.</summary>
        public double LockRun;
        /// <summary>True on the one frame the lock-in fired (for its burst).</summary>
        public bool LockInFired;
        /// <summary>True on the one frame attention was lost (the stutter or the fade starts).</summary>
        public bool Lost;
    }

    /// <summary>
    /// The pure maths of Super Lights Down (rides the mandatory video). The constants are copied
    /// from <c>mkWatch</c> in handoff-1001/super-effects-mockup.html: depth rises toward 0.92 at
    /// 0.55/s (exponential) while the video is watched and falls at 1.3/s when it is not; at
    /// 0.8 one light runs around the frame; looking away stutters the lights three times over
    /// 0.65 s with a red/cyan split and a lens streak. WPF-free and App-free so it is tested.
    /// </summary>
    public sealed class LightsDownState
    {
        public const double DepthMax = 0.92;
        public const double RiseRate = 0.55;      // exponential approach per second
        public const double FallRate = 1.3;       // linear per second
        public const double StutterSeconds = 0.65;
        // Three dips in 0.65 s (the mockup's sin(fa*36) gave three and a bit): lights on for the
        // first half of each third, off for the second, so the last dip ends as the stutter does.
        public const double StutterFreq = 2 * Math.PI * 3 / StutterSeconds;
        public const double StutterLow = 0.15;
        public const double PhotosafeFadeSeconds = 0.4;
        public const double SplitSeconds = 0.25;
        public const double StreakSeconds = 0.22;
        public const double LockInDepth = 0.8;
        public const double LockRunSeconds = 1.1;
        public const double BeatFrom = 0.5;
        public const double BeatFreq = 1.7;
        public const double BeatAmp = 0.05;
        public const double DimPerDepth = 0.5;
        public const double ApertureLayerAlpha = 0.4;
        public const double ApertureSpin = 0.04;  // rad/s per unit depth
        public const double SwellPerDepth = 0.07;
        public const double RaySpin = 0.1;        // rad/s
        public const double RayPeak = 0.12;

        private double _t;
        private double _lostAt = double.NegativeInfinity;
        private double _lockAt = double.NegativeInfinity;
        private bool _lastAttended = true;
        private bool _wasHigh;
        private bool _stutterThisLoss;
        private double _fadeFrom;
        private double _rot = 0.2;
        private double _rayAngle;
        private double _huePhase;
        private double _beatPhase;

        /// <summary>Raw depth (before the stutter).</summary>
        public double Depth { get; private set; }

        /// <summary>Seconds since the state was created (advanced by <see cref="Step"/>).</summary>
        public double Time => _t;

        /// <summary>
        /// Advance one frame. <paramref name="attended"/> = the cursor or gaze is on the picture
        /// and the video is not held. <paramref name="photosafe"/> swaps the stutter, the split
        /// and the streak for a single 0.4 s fade.
        /// </summary>
        public LightsDownFrame Step(double dt, bool attended, LightsDownMotion motion, bool photosafe)
        {
            if (double.IsNaN(dt) || dt < 0) dt = 0;
            dt = Math.Min(dt, 0.25); // a hitch must not jump the room to black
            _t += dt;

            double speed = motion == LightsDownMotion.Reduced ? 0.5 : motion == LightsDownMotion.Off ? 0 : 1;
            bool calm = photosafe || motion != LightsDownMotion.Full;

            var f = new LightsDownFrame { LockRun = -1 };

            if (!attended && _lastAttended)
            {
                _lostAt = _t;
                _wasHigh = false;
                _fadeFrom = Depth;
                _stutterThisLoss = !calm && Depth > 0.05;
                f.Lost = Depth > 0.05;
            }
            _lastAttended = attended;

            if (attended)
                Depth += (DepthMax - Depth) * (1 - Math.Exp(-dt * RiseRate));
            else if (calm)
                Depth = Math.Max(0, Depth - dt * Math.Max(FallRate, _fadeFrom / PhotosafeFadeSeconds));
            else
                Depth = Math.Max(0, Depth - dt * FallRate);

            double fa = _t - _lostAt;
            double d = Depth;
            if (_stutterThisLoss && fa >= 0 && fa < StutterSeconds)
                d = Depth * (Math.Sin(fa * StutterFreq) >= 0 ? 1 : StutterLow);
            if (_stutterThisLoss && fa >= 0 && fa < SplitSeconds) f.SplitAlpha = 0.35 * (1 - fa / SplitSeconds);
            if (_stutterThisLoss && fa >= 0 && fa < StreakSeconds) f.StreakAlpha = 0.5 * (1 - fa / StreakSeconds);

            _rot += dt * ApertureSpin * d * speed;
            _rayAngle += dt * RaySpin * speed;
            _huePhase += dt * 0.5 * speed;
            _beatPhase += dt * BeatFreq * speed;

            f.Depth = d;
            f.Beat = speed > 0 && d > BeatFrom ? Math.Pow(Math.Max(0, Math.Sin(_beatPhase)), 10) * BeatAmp * d * speed : 0;
            f.DimAlpha = DimPerDepth * d + f.Beat;
            f.ApertureAlpha = ApertureLayerAlpha * d;
            // Off = still: the aperture never moves, it sits closed and fades in with depth.
            f.ApertureClose = motion == LightsDownMotion.Off ? 1 : Ease(d);
            f.ApertureRotation = _rot;
            f.Hue = 300 + 40 * Math.Sin(_huePhase);
            f.RayAngle = _rayAngle;
            f.RayAlpha = RayPeak * d;
            f.Swell = 1 + SwellPerDepth * d * speed;
            // The mockup's glow never fully leaves; on a real screen at rest it must, so it rides
            // a short ramp in from depth 0.
            double glowIn = Math.Clamp(d / 0.2, 0, 1);
            f.GlowAlpha = (0.35 + 0.55 * d) * glowIn;
            f.GlowBlur = 26 + 90 * d;

            if (d > LockInDepth && !_wasHigh && attended)
            {
                _wasHigh = true;
                if (speed > 0) { _lockAt = _t; f.LockInFired = true; }
            }
            double lockAge = _t - _lockAt;
            if (lockAge >= 0 && lockAge < LockRunSeconds) f.LockRun = lockAge / LockRunSeconds;

            return f;
        }

        /// <summary>Smoothstep, the mockup's <c>ease</c>.</summary>
        public static double Ease(double x)
        {
            x = Math.Clamp(x, 0, 1);
            return x * x * (3 - 2 * x);
        }
    }

    /// <summary>Geometry helpers for Lights Down. Pure; units are whatever the caller uses (DIPs).</summary>
    public static class LightsDownMath
    {
        public const int ApertureLayers = 4;
        public const double ApertureLayerStep = 0.06;
        public const double ApertureOpen = 0.95;   // x max(screen w, h)
        public const double ApertureShut = 0.64;   // x picture width
        public const int Rays = 9;
        public const double RayHalfWidth = 0.07;   // rad
        public const double MotesPerSecond = 14;   // x depth
        public const double MoteLife = 3;

        /// <summary>Contain-fit a picture of <paramref name="aspect"/> (w/h) centred in the screen.
        /// Unknown aspect (0) fills the screen.</summary>
        public static (double X, double Y, double W, double H) FitPicture(double screenW, double screenH, double aspect)
        {
            if (screenW <= 0 || screenH <= 0) return (0, 0, 0, 0);
            if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect)) return (0, 0, screenW, screenH);
            double sa = screenW / screenH;
            double w, h;
            if (aspect >= sa) { w = screenW; h = screenW / aspect; }
            else { h = screenH; w = screenH * aspect; }
            return ((screenW - w) / 2, (screenH - h) / 2, w, h);
        }

        /// <summary>Radius of aperture layer <paramref name="layer"/> (0..3): from 0.95 x the screen's
        /// long side down to 0.64 x the picture width as <paramref name="close"/> goes 0 -> 1.</summary>
        public static double ApertureRadius(double screenW, double screenH, double pictureW, double close, int layer)
        {
            double open = Math.Max(screenW, screenH) * ApertureOpen;
            double shut = pictureW * ApertureShut;
            double r = open + (shut - open) * Math.Clamp(close, 0, 1);
            return r * (1 + layer * ApertureLayerStep);
        }

        /// <summary>Vertex <paramref name="i"/> (0..5) of a hexagon of circumradius 1 at rotation 0.</summary>
        public static (double X, double Y) HexVertex(int i)
        {
            double a = i * Math.PI * 2 / 6;
            return (Math.Cos(a), Math.Sin(a));
        }

        /// <summary>Per-ray shimmer, 0.6..1 of the peak, phased by ray index (mockup: .6+.4 sin(2t+i)).</summary>
        public static double RayAlpha(double peak, double time, int ray)
            => peak * (0.6 + 0.4 * Math.Sin(time * 2 + ray));

        /// <summary>Alpha of a dust mote of age <paramref name="age"/> seconds at depth <paramref name="depth"/>.</summary>
        public static double MoteAlpha(double age, double depth)
            => age < 0 || age >= MoteLife ? 0 : 0.55 * Math.Sin(Math.Min(1, age / MoteLife) * Math.PI) * depth;

        /// <summary>
        /// How many motes to spawn this frame: the expected count dt*14*depth, with the fraction
        /// settled by <paramref name="roll"/> in [0,1) so the rate is exact on average and
        /// deterministic under test.
        /// </summary>
        public static int MotesToSpawn(double dt, double depth, double roll)
        {
            double want = Math.Max(0, dt) * MotesPerSecond * Math.Max(0, depth);
            int whole = (int)Math.Floor(want);
            return whole + (roll < want - whole ? 1 : 0);
        }

        /// <summary>HSL (hue in degrees, s and l 0..1) to 0..255 RGB, for the light's drifting hue.</summary>
        public static (byte R, byte G, byte B) Hsl(double hue, double s, double l)
        {
            hue = ((hue % 360) + 360) % 360;
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
            double m = l - c / 2;
            (double r, double g, double b) = hue switch
            {
                < 60 => (c, x, 0.0),
                < 120 => (x, c, 0.0),
                < 180 => (0.0, c, x),
                < 240 => (0.0, x, c),
                < 300 => (x, 0.0, c),
                _ => (c, 0.0, x),
            };
            static byte B8(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
            return (B8(r + m), B8(g + m), B8(b + m));
        }

        /// <summary>Is the point inside the rectangle (edges inclusive).</summary>
        public static bool Inside(double px, double py, double x, double y, double w, double h)
            => w > 0 && h > 0 && px >= x && px <= x + w && py >= y && py <= y + h;

        /// <summary>
        /// May Lights Down draw at all right now. Panic and the emergency exit tear the video
        /// down, so a video that is not playing (or is mid teardown) ends the effect on the
        /// same tick: never more permissive than the base effect it rides.
        /// </summary>
        public static bool ShouldRun(bool superOn, bool videoPlaying, bool videoCleaningUp, bool hasWindows)
            => superOn && videoPlaying && !videoCleaningUp && hasWindows;

        /// <summary>The attention signal: gaze wins when the tracker is running, calibrated and its
        /// last sample is fresh; otherwise the cursor decides. A held (grace-paused) video is never watched.</summary>
        public static bool Attended(bool gracePaused, bool gazeLive, bool gazeOnPicture, bool cursorOnPicture)
            => !gracePaused && (gazeLive ? gazeOnPicture : cursorOnPicture);
    }
}
