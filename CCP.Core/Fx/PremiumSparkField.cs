using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Fx
{
    /// <summary>What a spark particle is, which decides its shape and how it moves.</summary>
    public enum MoteKind
    {
        /// <summary>Basic: a gold fleck that spills off a point and falls a little.</summary>
        Glitter,
        /// <summary>Basic: a small twinkle that circles the card on an ellipse.</summary>
        Orbit,
        /// <summary>Both: a white four-point twinkle that pops on a point and goes.</summary>
        Glint,
        /// <summary>Prime: a cyan diamond that drifts up and out of the card.</summary>
        Diamond,
        /// <summary>Prime: a bigger, slower flare on a tip or the heart of the star.</summary>
        Flare,
        /// <summary>A click (or a tier raise) throws these out radially.</summary>
        Burst,
    }

    /// <summary>One live particle, in the card's own pixels (54 x 42, star centre at 27,15).</summary>
    public struct SparkMote
    {
        public MoteKind Kind;
        public double X, Y, Vx, Vy;
        public double Age, Life;
        public double Size, Angle, Spin;
        public double Phase;
        /// <summary>0 = the tier's mote colour, 1 = white, 2 = the tier's deep colour.</summary>
        public int Colour;
        // orbit only
        public double OrbitA, OrbitSpeed, OrbitRx, OrbitRy;

        public readonly double T => Life <= 0 ? 1 : Math.Clamp(Age / Life, 0, 1);
    }

    /// <summary>
    /// THE SPARK'S PARTICLE FIELD, pure (polish 12 round 2, owner 2026-10-07: "there aren't
    /// enough particles"). One clock steps it; the control only draws what is alive. Basic spills
    /// a steady trickle of gold glitter off the points, keeps two or three twinkles orbiting the
    /// card and pops white glints on the tips. Prime runs a denser field of cyan diamonds drifting
    /// up and out, twinkling stars on the tips and, every few seconds, a bigger flare that throws
    /// a ring of diamonds. Free and Off spawn nothing; Reduced keeps a slow few.
    ///
    /// <para>Everything is capped (<see cref="Cap"/> alive, plus a fixed headroom for click
    /// bursts) so the pool of shapes the control keeps never grows. The rng is injected so tests
    /// and offscreen frame strips are repeatable.</para>
    /// </summary>
    public sealed class PremiumSparkField
    {
        // ---- card geometry (card px) ---------------------------------------------------------
        public const double CentreX = PremiumSparkRules.ControlWidth / 2;
        public const double CentreY = PremiumSparkRules.StarSize / 2;

        /// <summary>The four tips: top, right, bottom, left (card px), and their outward unit.</summary>
        public static readonly (double x, double y, double ux, double uy)[] Tips =
        {
            (CentreX, 0.0, 0, -1),
            (CentreX + 13.0, CentreY, 1, 0),
            (CentreX, PremiumSparkRules.StarSize, 0, 1),
            (CentreX - 13.0, CentreY, -1, 0),
        };

        // ---- budgets ---------------------------------------------------------------------------
        /// <summary>Most click-burst particles alive on top of the ambient cap.</summary>
        public const int BurstHeadroom = 18;

        private readonly Random _rng;
        private readonly List<SparkMote> _alive = new();
        private SparkTier _tier = SparkTier.Free;
        private SparkMotion _motion = SparkMotion.Off;
        private bool _particles;

        private double _nextSpawn, _nextGlint, _nextFlare, _nextOrbit;
        private double _flareGlow;

        public PremiumSparkField(Random rng) => _rng = rng;

        /// <summary>Particles alive right now, oldest first.</summary>
        public IReadOnlyList<SparkMote> Alive => _alive;

        /// <summary>The ambient cap for the current look (bursts ride on top, see BurstHeadroom).</summary>
        public int Cap => PremiumSparkRules.AmbientCap(_tier, _motion, _particles);

        /// <summary>The pool size the control needs: the biggest cap any look can ask for, plus bursts and one flare.</summary>
        public static int PoolSize => PremiumSparkRules.MaxAmbientCap + BurstHeadroom + 1;

        /// <summary>0..1, high for a moment after a flare: the control brightens Prime's halo with it.</summary>
        public double FlareGlow => _flareGlow;

        public void Configure(SparkTier tier, SparkMotion motion, bool particlesAllowed)
        {
            if (tier == _tier && motion == _motion && particlesAllowed == _particles) return;
            _tier = tier;
            _motion = motion;
            _particles = particlesAllowed;
            Clear();
        }

        public void Clear()
        {
            _alive.Clear();
            _nextSpawn = 0.05;
            _nextGlint = 0.3;
            _nextOrbit = 0.0;
            _nextFlare = Between(1.2, 2.4);
            _flareGlow = 0;
        }

        /// <summary>Advance the field by dt seconds: age and move every particle, drop the dead,
        /// spawn what the look asks for under its cap.</summary>
        public void Step(double dt)
        {
            if (dt <= 0) return;
            dt = Math.Min(dt, 0.1);   // a stalled frame must not teleport the field

            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                var m = _alive[i];
                m.Age += dt;
                if (m.Age >= m.Life) { _alive.RemoveAt(i); continue; }
                Move(ref m, dt);
                _alive[i] = m;
            }

            _flareGlow = Math.Max(0, _flareGlow - dt * 1.6);

            int cap = Cap;
            if (cap <= 0) return;
            double pace = _motion == SparkMotion.Reduced ? PremiumSparkRules.ReducedPace : 1.0;

            _nextSpawn -= dt;
            while (_nextSpawn <= 0)
            {
                if (AmbientCount() < cap) SpawnAmbient();
                _nextSpawn += Between(PremiumSparkRules.SpawnGapMin(_tier), PremiumSparkRules.SpawnGapMax(_tier)) / pace;
            }

            _nextGlint -= dt;
            if (_nextGlint <= 0)
            {
                if (AmbientCount() < cap) SpawnGlint();
                _nextGlint = Between(PremiumSparkRules.GlintGapMin(_tier), PremiumSparkRules.GlintGapMax(_tier)) / pace;
            }

            if (_motion != SparkMotion.Full) return;

            if (_tier == SparkTier.Basic)
            {
                _nextOrbit -= dt;
                if (_nextOrbit <= 0)
                {
                    if (CountKind(MoteKind.Orbit) < PremiumSparkRules.Orbiters && AmbientCount() < cap) SpawnOrbit();
                    _nextOrbit = Between(0.5, 1.1);
                }
            }
            else if (_tier == SparkTier.Prime)
            {
                _nextFlare -= dt;
                if (_nextFlare <= 0)
                {
                    SpawnFlare();
                    _nextFlare = Between(PremiumSparkRules.FlareGapMin, PremiumSparkRules.FlareGapMax);
                }
            }
        }

        /// <summary>A click: throws count particles out radially from the heart of the star.</summary>
        public void Burst(int count)
        {
            count = Math.Min(count, BurstHeadroom - CountKind(MoteKind.Burst));
            for (int i = 0; i < count; i++)
            {
                var angle = Math.PI * 2 * i / Math.Max(1, count) + _rng.NextDouble() * 0.4;
                var speed = 70 + _rng.NextDouble() * 60;
                _alive.Add(new SparkMote
                {
                    Kind = MoteKind.Burst,
                    X = CentreX, Y = CentreY,
                    Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed,
                    Life = 0.45 + _rng.NextDouble() * 0.2,
                    Size = _tier == SparkTier.Prime ? 3 + _rng.NextDouble() * 2.5 : 2 + _rng.NextDouble() * 2,
                    Angle = _rng.NextDouble() * 360, Spin = (_rng.NextDouble() - 0.5) * 720,
                    Colour = i % 3 == 0 ? 1 : 0,
                });
            }
        }

        // ---- spawning ---------------------------------------------------------------------------

        private void SpawnAmbient()
        {
            if (_tier == SparkTier.Prime) SpawnDiamond();
            else SpawnGlitter();
        }

        /// <summary>Gold glitter leaves a tip along its arm, then gravity takes it.</summary>
        private void SpawnGlitter()
        {
            var tip = Tips[PickTip()];
            var along = 1 + _rng.NextDouble() * 4;      // a little way in from the very tip
            var side = (_rng.NextDouble() - 0.5) * 3;
            var speed = 8 + _rng.NextDouble() * 12;
            var spread = (_rng.NextDouble() - 0.5) * 1.1;
            var ux = tip.ux * Math.Cos(spread) - tip.uy * Math.Sin(spread);
            var uy = tip.ux * Math.Sin(spread) + tip.uy * Math.Cos(spread);
            _alive.Add(new SparkMote
            {
                Kind = MoteKind.Glitter,
                X = tip.x - tip.ux * along + tip.uy * side,
                Y = tip.y - tip.uy * along + tip.ux * side,
                Vx = ux * speed, Vy = uy * speed - 4,
                Life = 1.0 + _rng.NextDouble() * 1.0,
                Size = 2.0 + _rng.NextDouble() * 1.6,
                Angle = _rng.NextDouble() * 90, Spin = (_rng.NextDouble() - 0.5) * 400,
                Phase = _rng.NextDouble() * Math.PI * 2,
                Colour = _rng.Next(5) switch { 0 => 1, 1 => 2, _ => 0 },
            });
        }

        /// <summary>A cyan diamond rises out of the card, drifting away from the centre.</summary>
        private void SpawnDiamond()
        {
            var a = _rng.NextDouble() * Math.PI * 2;
            var r = 3 + _rng.NextDouble() * 9;
            var x = CentreX + Math.Cos(a) * r * 1.1;
            var y = CentreY + Math.Sin(a) * r;
            var speed = 7 + _rng.NextDouble() * 13;
            _alive.Add(new SparkMote
            {
                Kind = MoteKind.Diamond,
                X = x, Y = y,
                Vx = Math.Cos(a) * speed, Vy = Math.Sin(a) * speed * 0.7 - (6 + _rng.NextDouble() * 6),
                Life = 1.2 + _rng.NextDouble() * 1.2,
                Size = 2.4 + _rng.NextDouble() * 2.4,
                Angle = (_rng.NextDouble() - 0.5) * 30, Spin = (_rng.NextDouble() - 0.5) * 60,
                Phase = _rng.NextDouble() * Math.PI * 2,
                Colour = _rng.Next(5) switch { 0 => 1, 1 or 2 => 2, _ => 0 },   // white, deep cyan, ice
            });
        }

        /// <summary>A twinkle on a tip (Prime: cyan and white stars; Basic: white glints).</summary>
        private void SpawnGlint()
        {
            var t = Tips[_rng.Next(Tips.Length)];
            // Prime stars sit right on the tip; Basic glints may also land along an arm.
            var inset = _tier == SparkTier.Prime ? 0.5 : _rng.NextDouble() * 6;
            _alive.Add(new SparkMote
            {
                Kind = MoteKind.Glint,
                X = t.x - t.ux * inset, Y = t.y - t.uy * inset,
                Life = 0.45 + _rng.NextDouble() * 0.2,
                Size = _tier == SparkTier.Prime ? 6 + _rng.NextDouble() * 3 : 5 + _rng.NextDouble() * 3,
                Angle = _rng.Next(0, 30), Spin = 90,
                Colour = _tier == SparkTier.Prime && _rng.Next(2) == 0 ? 0 : 1,
            });
        }

        private void SpawnOrbit()
        {
            _alive.Add(new SparkMote
            {
                Kind = MoteKind.Orbit,
                OrbitA = _rng.NextDouble() * Math.PI * 2,
                OrbitSpeed = (1.1 + _rng.NextDouble() * 0.7) * (_rng.Next(2) == 0 ? 1 : -1),
                OrbitRx = 17 + _rng.NextDouble() * 5, OrbitRy = 9 + _rng.NextDouble() * 4,
                Life = 2.4 + _rng.NextDouble() * 1.6,
                Size = 3 + _rng.NextDouble() * 1.5,
                Angle = 0, Spin = 120,
                Phase = _rng.NextDouble() * Math.PI * 2,
                Colour = _rng.Next(3) == 0 ? 1 : 0,
            });
            var last = _alive[^1];
            Move(ref last, 0);
            _alive[^1] = last;
        }

        /// <summary>Prime's flare: a big star on a tip or the heart, and a ring of diamonds off it.</summary>
        private void SpawnFlare()
        {
            bool heart = _rng.Next(3) == 0;
            var t = Tips[_rng.Next(Tips.Length)];
            double fx = heart ? CentreX : t.x, fy = heart ? CentreY : t.y;
            _alive.Add(new SparkMote
            {
                Kind = MoteKind.Flare,
                X = fx, Y = fy,
                Life = PremiumSparkRules.FlareSec,
                Size = 15 + _rng.NextDouble() * 4,
                Angle = 0, Spin = 70,
                Colour = 1,
            });
            _flareGlow = 1;
            int ring = Math.Min(6, BurstHeadroom - CountKind(MoteKind.Burst));
            for (int i = 0; i < ring; i++)
            {
                var a = Math.PI * 2 * i / 6 + _rng.NextDouble() * 0.3;
                var speed = 30 + _rng.NextDouble() * 18;
                _alive.Add(new SparkMote
                {
                    Kind = MoteKind.Burst,
                    X = fx, Y = fy,
                    Vx = Math.Cos(a) * speed, Vy = Math.Sin(a) * speed,
                    Life = 0.6 + _rng.NextDouble() * 0.25,
                    Size = 2 + _rng.NextDouble() * 1.5,
                    Angle = 0, Spin = (_rng.NextDouble() - 0.5) * 200,
                    Colour = i % 2,
                });
            }
        }

        // ---- motion ---------------------------------------------------------------------------

        private static void Move(ref SparkMote m, double dt)
        {
            switch (m.Kind)
            {
                case MoteKind.Glitter:
                    m.Vy += 26 * dt;                 // it spills: gravity pulls it down
                    m.Vx *= 1 - 0.6 * dt;
                    break;
                case MoteKind.Diamond:
                    m.Vx *= 1 - 0.4 * dt;
                    m.Vy -= 3 * dt;                  // a soft lift, it floats away
                    break;
                case MoteKind.Burst:
                    m.Vx *= 1 - 4.2 * dt;            // thrown hard, drag stops it quickly
                    m.Vy *= 1 - 4.2 * dt;
                    m.Vy += 20 * dt;
                    break;
                case MoteKind.Orbit:
                    m.OrbitA += m.OrbitSpeed * dt;
                    m.X = CentreX + Math.Cos(m.OrbitA) * m.OrbitRx;
                    m.Y = CentreY + 1 + Math.Sin(m.OrbitA) * m.OrbitRy;
                    m.Angle += m.Spin * dt;
                    return;
            }
            m.X += m.Vx * dt;
            m.Y += m.Vy * dt;
            m.Angle += m.Spin * dt;
        }

        /// <summary>How opaque a particle is now: a quick fade in, a twinkle for the kinds that
        /// twinkle, and a fade out over the last part of its life.</summary>
        public static double OpacityOf(in SparkMote m)
        {
            var t = m.T;
            double env = t < 0.12 ? t / 0.12 : t > 0.7 ? (1 - t) / 0.3 : 1;
            switch (m.Kind)
            {
                case MoteKind.Glitter:
                case MoteKind.Diamond:
                case MoteKind.Orbit:
                    env *= 0.68 + 0.32 * Math.Sin(m.Phase + m.Age * 13);
                    break;
                case MoteKind.Glint:
                case MoteKind.Flare:
                    env = 1;   // these grow and shrink instead (ScaleOf)
                    break;
            }
            return Math.Clamp(env, 0, 1);
        }

        /// <summary>Glints and flares pop: grow to full by 40% of their life, then shrink away.</summary>
        public static double ScaleOf(in SparkMote m)
        {
            if (m.Kind != MoteKind.Glint && m.Kind != MoteKind.Flare) return 1;
            var t = m.T;
            if (t < 0.4)
            {
                var u = t / 0.4;
                return 1 - Math.Pow(1 - u, 3) * (1 - u * 0.3);   // eased out with a small kick
            }
            return Math.Max(0, 1 - (t - 0.4) / 0.6);
        }

        // ---- helpers ------------------------------------------------------------------------------

        private int AmbientCount()
        {
            int n = 0;
            foreach (var m in _alive) if (m.Kind != MoteKind.Burst && m.Kind != MoteKind.Flare) n++;
            return n;
        }

        public int CountKind(MoteKind kind)
        {
            int n = 0;
            foreach (var m in _alive) if (m.Kind == kind) n++;
            return n;
        }

        /// <summary>Glitter leaves the long top and bottom points more often than the side ones.</summary>
        private int PickTip() => _rng.Next(10) switch { < 3 => 0, < 6 => 2, < 8 => 1, _ => 3 };

        private double Between(double min, double max) => PremiumSparkRules.Between(_rng, min, max);
    }
}
