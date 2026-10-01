using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>One word's ghost: where the card put it, and when it settled.</summary>
    public sealed class AfterglowGhost
    {
        public string Text = "";
        /// <summary>Which screen placement it belongs to (the per-screen cap counts by this).</summary>
        public int Screen;
        /// <summary>Centre of the word now, world (virtual-desktop) px.</summary>
        public double X, Y;
        /// <summary>Where the card showed it, and where the ghost drifts to rest.</summary>
        public double FromX, FromY, RestX, RestY;
        /// <summary>Seconds the drift from the card to the rest spot takes (0 = no drift).</summary>
        public double DriftS;
        /// <summary>The card's font size on this screen, px. Every distance scales off it.</summary>
        public double FontPx;
        /// <summary>The card's own opacity setting, 0..1. The ghost never outshines its card.</summary>
        public double Opacity = 1;
        /// <summary>Field clock (s) when the ghost takes over from the card. Before it, inert and unseen.</summary>
        public double Born;
        /// <summary>Field clock (s) of the last wake.</summary>
        public double Wake = -9;
        /// <summary>The layer's draw cache (measured runs). Never read by the maths.</summary>
        public object? Payload;
    }

    /// <summary>A spark or an ember. Struct, pooled: no allocation per frame.</summary>
    public struct AfterglowParticle
    {
        public double X, Y, Vx, Vy, T, Life, Unit;
    }

    /// <summary>A wake's shock ring.</summary>
    public struct AfterglowRing
    {
        public double X, Y, T0, FontPx;
    }

    /// <summary>
    /// Super Afterglow, the maths (mockup <c>mkSubs</c>, owner rated 10/10, ported as-is). After a
    /// subliminal card, its word stays where it appeared as a faint pink ghost that fades over 4 s
    /// and sheds embers. Bring the cursor near a ghost and it flares back with sparks and a shock
    /// ring; a woken word weighs a little more in the next pick.
    ///
    /// Mockup distances are canvas px around a 34 px font. Real cards are 120 DIP, so every
    /// speed and size here is multiplied by <c>unit = FontPx / 34</c>: the proportions the owner
    /// saw survive the bigger word.
    ///
    /// WPF-free, Skia-free, App-free. The layer draws what this leaves in <see cref="Ghosts"/>,
    /// <see cref="Particles"/> and <see cref="Rings"/>.
    /// </summary>
    public sealed class AfterglowField
    {
        public const double MockFontPx = 34;
        public const double LifeS = 4;            // base = 1 - age / 4
        public const double DropS = 4.2;          // ghosts leave the list here
        public const double IntroS = 0.16;        // 1 -> 0.35 as the card hands over
        public const double GhostAlpha = 0.35;
        public const int MaxPerScreen = 6;
        public const double WakeRadius = 1.6;     // x font size
        public const double WakeMinAge = 0.5;
        public const double WakeCooldownS = 1.5;
        public const double WakeMinBase = 0.15;
        public const double FlareS = 0.4;
        public const double FlareOffS = 0.12;     // MotionLevel.Off: the 120 ms fade
        public const double PhotosafeFlarePeak = 0.6;
        public const int SparkCount = 14;
        public const double SparkSpeed = 110;     // x (0.3..1)
        public const double SparkLife = 0.6;      // x (0.6..1.2)
        public const double EmberMinAge = 0.3;
        public const double EmberRate = 3;        // per second, x base
        public const double EmberLife = 1.2;
        public const double EmberRiseMin = 18, EmberRiseSpan = 14;   // 18..32 px/s up
        public const double EmberDrift = 10;      // +-5 px/s sideways
        public const double RingS = 0.6;
        public const double RingFrom = 0.6, RingGrow = 2.2;          // radius 0.6 -> 2.8 x font
        public const double Drag = 2;             // v *= 1 - dt * 2
        public const int MaxParticles = 400;
        // Real cards all sit at the screen centre, so ghosts left in place would stack into one
        // smear. Each ghost drifts to its own spot in the mockup's band (x .18..82, y .2..8 of
        // the screen), picking the farthest of a few tries from the ghosts already resting there.
        public const double DriftS = 0.9;          // Reduced: twice as long, half as far
        public const double BandX0 = 0.18, BandXSpan = 0.64, BandY0 = 0.2, BandYSpan = 0.6;
        public const int RestTries = 4;

        public readonly List<AfterglowGhost> Ghosts = new();
        public readonly AfterglowParticle[] Particles = new AfterglowParticle[MaxParticles];
        public int ParticleCount;
        public readonly List<AfterglowRing> Rings = new();

        /// <summary>Seconds since the field started; advanced only by <see cref="Step"/>.</summary>
        public double Now { get; private set; }

        public bool IsEmpty => Ghosts.Count == 0 && ParticleCount == 0 && Rings.Count == 0;

        /// <summary>Unit scale for a font size (see class remarks).</summary>
        public static double Unit(double fontPx) => Math.Max(0.1, fontPx / MockFontPx);

        /// <summary>Half speed and amplitude at Reduced (CONTRACT rule 3).</summary>
        public static double MotionScale(MotionLevel level) => level == MotionLevel.Reduced ? 0.5 : 1.0;

        /// <summary>1 at settle, 0 at <see cref="LifeS"/>.</summary>
        public static double Base(double age) => Math.Max(0, 1 - age / LifeS);

        /// <summary>
        /// The ghost's alpha (before the card's own opacity): the 160 ms hand-over from the card,
        /// then 0.35 x base, raised to the wake flare while one runs. Negative age = not yet born.
        /// </summary>
        public static double Alpha(double age, double sinceWake, MotionLevel level, bool photosafe)
        {
            if (age < 0) return 0;
            double a = age < IntroS ? 1 - age / IntroS * (1 - GhostAlpha) : Base(age) * GhostAlpha;
            double flareS = level == MotionLevel.Off ? FlareOffS : FlareS;
            if (sinceWake >= 0 && sinceWake < flareS)
            {
                double peak = photosafe ? PhotosafeFlarePeak : 1;
                a = Math.Max(a, peak * (1 - sinceWake / flareS));
            }
            return Math.Clamp(a, 0, 1);
        }

        /// <summary>The wake rule: close enough, settled, rested, and not already mostly gone.</summary>
        public static bool ShouldWake(double distPx, double fontPx, double age, double sinceWake, double baseLeft)
            => distPx < fontPx * WakeRadius && age > WakeMinAge && sinceWake > WakeCooldownS && baseLeft > WakeMinBase;

        /// <summary>
        /// Weighted pick: each word weighs 1, plus its wake bonus. <paramref name="r01"/> in [0,1).
        /// With no bonus this is exactly a uniform pick (index = floor(r * n)).
        /// </summary>
        public static int PickWeighted(IReadOnlyList<string> texts, IReadOnlyDictionary<string, int>? bonus, double r01)
        {
            if (texts.Count == 0) return -1;
            double total = 0;
            for (int i = 0; i < texts.Count; i++) total += 1 + Bonus(bonus, texts[i]);
            double x = Math.Clamp(r01, 0, 0.999999999) * total;
            for (int i = 0; i < texts.Count; i++)
            {
                x -= 1 + Bonus(bonus, texts[i]);
                if (x < 0) return i;
            }
            return texts.Count - 1;
        }

        private static int Bonus(IReadOnlyDictionary<string, int>? bonus, string t)
            => bonus != null && bonus.TryGetValue(t, out var b) && b > 0 ? b : 0;

        /// <summary>
        /// A card went up. Its ghost takes over in <paramref name="delayS"/> (the moment the card
        /// starts to fade out). Over the per-screen cap, the oldest ghost on that screen goes.
        /// </summary>
        public AfterglowGhost Spawn(string text, int screen, double x, double y, double fontPx, double opacity, double delayS)
            => Spawn(text, screen, x, y, fontPx, opacity, delayS, 0, 0, 0, 0, 0, MotionLevel.Off, null);

        /// <summary>
        /// As above, and the ghost drifts from the card to a free spot inside the screen area
        /// (<paramref name="left"/>..<paramref name="right"/>, keeping <paramref name="halfW"/> clear of
        /// each side). MotionLevel.Off keeps it where the card was (still); Reduced goes half as far
        /// at half the speed.
        /// </summary>
        public AfterglowGhost Spawn(string text, int screen, double x, double y, double fontPx, double opacity, double delayS,
            double left, double top, double right, double bottom, double halfW, MotionLevel level, Func<double>? rnd)
        {
            int onScreen = 0, oldest = -1;
            for (int i = 0; i < Ghosts.Count; i++)
            {
                if (Ghosts[i].Screen != screen) continue;
                onScreen++;
                if (oldest < 0 || Ghosts[i].Born < Ghosts[oldest].Born) oldest = i;
            }
            if (onScreen >= MaxPerScreen && oldest >= 0) Ghosts.RemoveAt(oldest);

            var g = new AfterglowGhost
            {
                Text = text, Screen = screen, X = x, Y = y, FontPx = fontPx,
                Opacity = Math.Clamp(opacity, 0, 1), Born = Now + Math.Max(0, delayS),
                FromX = x, FromY = y, RestX = x, RestY = y
            };
            if (rnd != null && level != MotionLevel.Off && right > left && bottom > top)
            {
                PickRest(screen, left, top, right, bottom, halfW, rnd, out var rx, out var ry);
                double k = MotionScale(level);
                g.RestX = x + (rx - x) * k;
                g.RestY = y + (ry - y) * k;
                g.DriftS = DriftS / k;
            }
            Ghosts.Add(g);
            return g;
        }

        /// <summary>The farthest of <see cref="RestTries"/> spots in the band from the other ghosts on this screen.</summary>
        private void PickRest(int screen, double left, double top, double right, double bottom, double halfW,
            Func<double> rnd, out double bestX, out double bestY)
        {
            double w = right - left, h = bottom - top;
            double lo = left + Math.Min(halfW, w / 2), hi = right - Math.Min(halfW, w / 2);
            bestX = (left + right) / 2; bestY = (top + bottom) / 2;
            double bestD = -1;
            for (int k = 0; k < RestTries; k++)
            {
                double cx = Math.Clamp(left + w * (BandX0 + rnd() * BandXSpan), lo, hi);
                double cy = top + h * (BandY0 + rnd() * BandYSpan);
                double d = double.MaxValue;
                foreach (var o in Ghosts)
                {
                    if (o.Screen != screen) continue;
                    double dx = o.RestX - cx, dy = o.RestY - cy;
                    d = Math.Min(d, dx * dx + dy * dy);
                }
                if (d > bestD) { bestD = d; bestX = cx; bestY = cy; }
            }
        }

        /// <summary>Ease-out drift 0..1 at <paramref name="age"/>; 1 when there is no drift.</summary>
        public static double DriftProgress(double age, double driftS)
        {
            if (driftS <= 0 || age >= driftS) return 1;
            if (age <= 0) return 0;
            double u = 1 - age / driftS;
            return 1 - u * u * u;
        }

        /// <summary>Panic, stop, switch off: everything goes, at once.</summary>
        public void Clear()
        {
            Ghosts.Clear();
            Rings.Clear();
            ParticleCount = 0;
        }

        /// <summary>
        /// Advance by <paramref name="dt"/> seconds. <paramref name="hasCursor"/> false = no wakes.
        /// Every woken ghost is appended to <paramref name="woken"/> (may be null).
        /// <paramref name="rnd"/> returns [0,1); injected so tests are deterministic.
        /// </summary>
        public void Step(double dt, bool hasCursor, double cursorX, double cursorY,
            MotionLevel level, Func<double> rnd, List<AfterglowGhost>? woken = null)
        {
            if (dt < 0) dt = 0;
            Now += dt;
            double t = Now;
            double ms = MotionScale(level);
            bool moving = level != MotionLevel.Off;

            for (int i = Ghosts.Count - 1; i >= 0; i--)
            {
                var g = Ghosts[i];
                double age = t - g.Born;
                if (age >= DropS) { Ghosts.RemoveAt(i); continue; }
                if (age < 0) continue;
                double baseLeft = Base(age), unit = Unit(g.FontPx);
                double dp = DriftProgress(age, g.DriftS);
                g.X = g.FromX + (g.RestX - g.FromX) * dp;
                g.Y = g.FromY + (g.RestY - g.FromY) * dp;

                if (hasCursor)
                {
                    double dx = cursorX - g.X, dy = cursorY - g.Y;
                    if (ShouldWake(Math.Sqrt(dx * dx + dy * dy), g.FontPx, age, t - g.Wake, baseLeft))
                    {
                        g.Wake = t;
                        woken?.Add(g);
                        if (moving)
                        {
                            for (int k = 0; k < SparkCount; k++)
                            {
                                double ang = rnd() * Math.PI * 2, sp = SparkSpeed * (0.3 + rnd() * 0.7) * unit * ms;
                                Emit(g.X, g.Y, Math.Cos(ang) * sp, Math.Sin(ang) * sp, SparkLife * (0.6 + rnd() * 0.6), unit);
                            }
                            Rings.Add(new AfterglowRing { X = g.X, Y = g.Y, T0 = t, FontPx = g.FontPx });
                        }
                    }
                }

                if (moving && age > EmberMinAge && rnd() < dt * EmberRate * baseLeft)
                {
                    Emit(g.X + (rnd() - 0.5) * g.FontPx * 2, g.Y,
                        (rnd() - 0.5) * EmberDrift * unit * ms,
                        -(EmberRiseMin + rnd() * EmberRiseSpan) * unit * ms,
                        EmberLife, unit);
                }
            }

            // Particles: drift, drag, age; compact in place (no allocation).
            int w = 0;
            double drag = Math.Max(0, 1 - dt * Drag);
            for (int i = 0; i < ParticleCount; i++)
            {
                var p = Particles[i];
                p.T += dt;
                if (p.T >= p.Life) continue;
                p.X += p.Vx * dt; p.Y += p.Vy * dt;
                p.Vx *= drag; p.Vy *= drag;
                Particles[w++] = p;
            }
            ParticleCount = w;

            for (int i = Rings.Count - 1; i >= 0; i--)
                if (t - Rings[i].T0 >= RingS) Rings.RemoveAt(i);
        }

        private void Emit(double x, double y, double vx, double vy, double life, double unit)
        {
            if (ParticleCount >= MaxParticles)
            {
                // Mockup parity: the oldest go first.
                Array.Copy(Particles, 1, Particles, 0, MaxParticles - 1);
                ParticleCount = MaxParticles - 1;
            }
            Particles[ParticleCount++] = new AfterglowParticle { X = x, Y = y, Vx = vx, Vy = vy, Life = life, Unit = unit };
        }

        /// <summary>Particle alpha, 1 at birth to 0 at end of life.</summary>
        public static double ParticleAlpha(in AfterglowParticle p) => Math.Max(0, 1 - p.T / p.Life);

        /// <summary>Ring progress 0..1 and radius at time <paramref name="t"/>.</summary>
        public static double RingRadius(in AfterglowRing r, double t, MotionLevel level)
        {
            double u = Math.Clamp((t - r.T0) / RingS, 0, 1);
            return r.FontPx * (RingFrom + u * RingGrow * MotionScale(level));
        }
    }
}
