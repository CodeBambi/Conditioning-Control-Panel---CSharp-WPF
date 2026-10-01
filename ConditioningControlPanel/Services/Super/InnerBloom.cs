using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// Super Inner Bloom (rides the ambient bubbles): the pure half. Every number here is the
    /// approved mockup's (`mkBubbles`, `film`, `pic`). No WPF, no Skia, no App: BubbleService
    /// decides when, BubbleLayer draws, this says where and how much.
    ///
    /// A bloom is a big film bubble carrying 2-3 mediums; a medium either wears a picture face
    /// (a leaf) or carries 2 smalls of its own (a carrier). Popping a bubble that carries kids
    /// squeezes it, tears the film, and releases the kids as ordinary picture bubbles. A small
    /// that is not popped within about a second escapes upward. Pop every released kid before
    /// any gets away and the bloom plays a CLEAN sweep (visual only, no reward).
    /// </summary>
    public static class InnerBloom
    {
        // ---- spawn ----
        /// <summary>About one ambient spawn in six becomes a bloom (one bloom alive at a time).</summary>
        public const int SpawnOneIn = 6;
        /// <summary>The big bubble's size against an ordinary ambient bubble's.</summary>
        public const double BigSizeMult = 2.4;
        /// <summary>The visible glass radius of a bubble sprite against the sprite's size (film radius = size x this).</summary>
        public const double GlassOfSprite = .45;
        /// <summary>A released kid is never smaller than this (DIP), so it stays catchable.</summary>
        public const int MinKidSizeDip = 44;

        // ---- kids inside ----
        public const double OrbitRadius = .42, OrbitWobble = .26, OrbitWobbleRate = 1.3, OrbitSpin = .7;
        public const double OrbitSquash = .85;
        public const double LeafRadius = .22, CarrierRadius = .30;
        /// <summary>A small's radius against its carrier's, and its orbit inside the carrier.</summary>
        public const double SmallInCarrier = .32, SmallOrbit = .42, SmallSpin = 1.4;

        // ---- film ----
        public const int FilmPoints = 56, FilmArcs = 14;
        public const double BulgeStart = .8, BulgeGain = .9, BulgeWidth = .4, FilmArcRadius = .88;

        // ---- pop ----
        public const double SqueezeS = .25, TearS = .35, SqueezeAmount = .07;
        public const int Droplets = 22;
        public const double ReleaseX = 100, ReleaseY = 80, ReleaseLift = 30, ReleaseEase = 2.4;
        /// <summary>A small rides out with its release for this long, then escapes if unpopped.</summary>
        public const double EscapeAfterS = 1.0, EscapeFadeS = 1.0, EscapeRise = 16;
        /// <summary>Motion Off: no tear, the film simply fades.</summary>
        public const double StillFadeS = .12;

        // ---- clean sweep ----
        public const int SweepSparks = 46;
        public const double SweepRingS = .5, SweepRingGrow = 2.4, CleanFloatS = 1.4;

        /// <summary>One bubble in a bloom. No kids = a leaf (it wears a picture). Kids = a carrier.</summary>
        public sealed class Node
        {
            public Node(int hue, IReadOnlyList<Node>? kids = null)
            {
                Hue = hue;
                Kids = kids ?? Array.Empty<Node>();
            }
            public int Hue { get; }
            public IReadOnlyList<Node> Kids { get; }
            public bool IsCarrier => Kids.Count > 0;
            /// <summary>The big bloom (kids orbit by <see cref="KidAt"/>); a carrier's smalls ride <see cref="SmallAt"/>.</summary>
            public bool IsRoot { get; set; }
            /// <summary>The picture path this leaf wears (picked by the service; null = film only).</summary>
            public string? PicturePath { get; set; }
            /// <summary>The decoded picture (a frozen WPF source, held as object so this stays WPF-free).</summary>
            public object? Picture { get; set; }
            /// <summary>Phase that spreads blooms apart, and the clock (ms) the orbit runs on.</summary>
            public double Seed { get; set; }
            public long ClockMs { get; set; }
            public double TimeAt(long nowMs) => Math.Max(0, nowMs - ClockMs) / 1000.0;

            /// <summary>Every bubble that a full pop-through releases, this one excluded.</summary>
            public int ReleasedCount()
            {
                int n = 0;
                foreach (var k in Kids) n += 1 + k.ReleasedCount();
                return n;
            }

            public IEnumerable<Node> Leaves()
            {
                if (!IsCarrier) { yield return this; yield break; }
                foreach (var k in Kids) foreach (var l in k.Leaves()) yield return l;
            }
        }

        /// <summary>Motion knobs: Full 1/1, Reduced half speed and amplitude, Off still.</summary>
        public readonly record struct Motion(double Speed, double Amp, bool Still)
        {
            public static Motion For(MotionLevel level) => level switch
            {
                MotionLevel.Off => new Motion(0, 0, true),
                MotionLevel.Reduced => new Motion(.5, .5, false),
                _ => new Motion(1, 1, false),
            };
        }

        public static bool RollSpawn(Random random) => random.Next(SpawnOneIn) == 0;

        /// <summary>A fresh bloom: 2-3 mediums, and in about half the blooms one medium carries 2 smalls.</summary>
        public static Node Plan(Random random)
        {
            int n = 2 + random.Next(2);
            int carrier = random.Next(2) == 0 ? random.Next(n) : -1;
            int hue0 = random.Next(360);
            var kids = new Node[n];
            for (int j = 0; j < n; j++)
            {
                int hue = (hue0 + j * 360 / n) % 360;
                kids[j] = j == carrier
                    ? new Node(hue, new[] { new Node((hue + 70) % 360), new Node((hue + 170) % 360) })
                    : new Node(hue);
            }
            return new Node(hue0, kids) { IsRoot = true };
        }

        /// <summary>Kid j of <paramref name="parent"/> (radius r): offset, throw angle and radius, whichever level it sits on.</summary>
        public static (double X, double Y, double Angle, double Radius) Place(Node parent, int j, double t, double r, Motion m)
        {
            if (parent.IsRoot)
            {
                var k = KidAt(j, parent.Kids.Count, t, r, parent.Seed, m);
                return (k.X, k.Y, k.Angle, KidRadius(parent.Kids[j], r));
            }
            var s = SmallAt(j, t, r, m);
            return (s.X, s.Y, Math.Atan2(s.Y, s.X), r * SmallInCarrier);
        }

        /// <summary>The bubble size (DIP) a released kid of radius <paramref name="radiusDip"/> spawns at.</summary>
        public static int ReleasedSizeDip(double radiusDip) => Math.Max(MinKidSizeDip, (int)Math.Round(radiusDip / GlassOfSprite));

        /// <summary>Radius of kid <paramref name="kid"/> inside a parent of radius R.</summary>
        public static double KidRadius(Node kid, double parentR) => parentR * (kid.IsCarrier ? CarrierRadius : LeafRadius);

        /// <summary>Kid j of n, orbiting inside a bubble of radius R: offset from the parent's
        /// centre, its angle and its orbit radius. <paramref name="seed"/> spreads blooms apart.</summary>
        public static (double X, double Y, double Angle, double Orbit) KidAt(int j, int n, double t, double r, double seed, Motion m)
        {
            double tt = t * m.Speed;
            double ang = j * Math.PI * 2 / Math.Max(1, n) + tt * OrbitSpin + seed;
            double orb = r * OrbitRadius * (1 + OrbitWobble * m.Amp * Math.Sin(tt * OrbitWobbleRate + j * 2));
            return (Math.Cos(ang) * orb, Math.Sin(ang) * orb * OrbitSquash, ang, orb);
        }

        /// <summary>A small inside its carrier (radius rc): offset from the carrier's centre.</summary>
        public static (double X, double Y) SmallAt(int s, double t, double rc, Motion m)
        {
            double a = s * Math.PI + t * SmallSpin * m.Speed;
            return (Math.Cos(a) * rc * SmallOrbit, Math.Sin(a) * rc * SmallOrbit);
        }

        public readonly record struct Bulge(double Angle, double Mag, double Width);

        /// <summary>Where the kids press on the film. Writes into <paramref name="into"/> (no allocation)
        /// and returns how many bulges it wrote.</summary>
        public static int Bulges(Node parent, double t, double r, double seed, Motion m, Span<Bulge> into)
        {
            int n = parent.Kids.Count, w = 0;
            if (!parent.IsRoot) return 0;   // a carrier's smalls sit well inside its film
            for (int j = 0; j < n && w < into.Length; j++)
            {
                var k = KidAt(j, n, t, r, seed, m);
                double ex = (k.Orbit + KidRadius(parent.Kids[j], r)) / r - BulgeStart;
                if (ex > 0) into[w++] = new Bulge(k.Angle, ex * BulgeGain, BulgeWidth);
            }
            return w;
        }

        /// <summary>The film's radius factor at angle <paramref name="a"/> (1 = round): a gaussian dent outward per bulge.</summary>
        public static double RadiusFactor(double a, ReadOnlySpan<Bulge> bulges)
        {
            double k = 1;
            foreach (var b in bulges)
            {
                double d = ((a - b.Angle) % (Math.PI * 2) + Math.PI * 3) % (Math.PI * 2) - Math.PI;
                k += b.Mag * Math.Exp(-(d * d) / (2 * b.Width * b.Width));
            }
            return k;
        }

        /// <summary>Squeeze before the tear: 0..1 over the last <see cref="SqueezeS"/> (smoothstep).</summary>
        public static double Squeeze(double sinceStart) => Smooth(Math.Clamp(sinceStart / SqueezeS, 0, 1));

        /// <summary>The tear's half-gap in radians at u seconds into the tear: 0 to a full circle in <see cref="TearS"/>.</summary>
        public static double TearGap(double u) => Math.Clamp(u / TearS, 0, 1) * Math.PI;

        /// <summary>How far a released kid has travelled from where it sat, in DIP (screen y down),
        /// u seconds after release: v = dir*(100,80) - 30 up, exponential ease 2.4.</summary>
        public static (double Dx, double Dy) ReleaseOffset(double angle, double u, Motion m)
        {
            if (m.Still || u <= 0) return (0, 0);
            double e = (1 - Math.Exp(-ReleaseEase * u)) / ReleaseEase * m.Amp;
            return (Math.Cos(angle) * ReleaseX * e, (Math.Sin(angle) * ReleaseY - ReleaseLift) * e - u * u * 3 * m.Amp);
        }

        /// <summary>A released small, u seconds out: its extra rise (DIP from release, up = negative)
        /// and its alpha. It fades out over the second after <see cref="EscapeAfterS"/>; alpha 0 = escaped.</summary>
        public static (double Dy, double Alpha) Escape(double u, Motion m)
        {
            if (u <= 0) return (0, 1);
            double dy = m.Still ? 0 : -(u * u * EscapeRise * m.Amp);
            return (dy, Math.Clamp(1 - (u - EscapeAfterS) / EscapeFadeS, 0, 1));
        }

        private static double Smooth(double x) => x * x * (3 - 2 * x);

        /// <summary>
        /// The clean-sweep rule: every bubble a bloom released must be popped by the player
        /// before any one of them escapes or drifts away. Reports clean exactly once.
        /// </summary>
        public sealed class Sweep
        {
            private int _left;
            private bool _lost, _reported;
            public Sweep(int released) { _left = Math.Max(0, released); }
            public bool Lost => _lost;
            public int Left => _left;
            /// <summary>A released bubble got away (escaped, floated off, dissolved).</summary>
            public void Lose() => _lost = true;
            /// <summary>The player popped a released bubble. True the one time the sweep turns clean.</summary>
            public bool Popped()
            {
                if (_left > 0) _left--;
                if (_left > 0 || _lost || _reported) return false;
                _reported = true;
                return true;
            }
        }
    }
}
