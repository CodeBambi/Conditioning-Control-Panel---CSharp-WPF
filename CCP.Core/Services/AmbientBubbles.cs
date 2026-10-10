using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The ambient-pop daily XP bucket (WPF BubbleService, feat/xp-economy, #1019/#1026): 5 XP a
    /// pop, lucky rolls from the same bucket, capped at 300 per local calendar day with LAZY
    /// rollover on the {day key, paid} pair in AppSettings. No Save() here - a per-pop disk write
    /// would be the expensive half of a 5 XP grant; the counter rides out with the next save.
    /// </summary>
    public static class AmbientBubbleXp
    {
        public const int DailyXpCap = 300;

        private static string Today => DateTime.Now.ToString("yyyy-MM-dd");

        /// <summary>Paid today, 0..cap. Read-only: a stale day key reads as 0 without mutating settings.</summary>
        public static int PaidToday(AppSettings? s)
        {
            if (s == null || !string.Equals(s.AmbientBubbleXpDayKey, Today, StringComparison.Ordinal)) return 0;
            return Math.Clamp(s.AmbientBubbleXpPaidToday, 0, DailyXpCap);
        }

        public static int RemainingToday(AppSettings? s) => Math.Max(0, DailyXpCap - PaidToday(s));

        /// <summary>Draw <paramref name="earnedXp"/> from the bucket; returns what it can actually pay
        /// (a lucky roll near the ceiling pays the remainder, not nothing).</summary>
        public static int Take(AppSettings? s, int earnedXp)
        {
            if (s == null) return earnedXp;
            var today = Today;
            if (!string.Equals(s.AmbientBubbleXpDayKey, today, StringComparison.Ordinal))
            {
                s.AmbientBubbleXpDayKey = today;
                s.AmbientBubbleXpPaidToday = 0;
            }
            var paidSoFar = Math.Max(0, s.AmbientBubbleXpPaidToday);   // hand-edited negatives read as 0
            var remaining = DailyXpCap - paidSoFar;
            if (remaining <= 0) return 0;
            var pay = Math.Min(earnedXp, remaining);
            s.AmbientBubbleXpPaidToday = paidSoFar + pay;
            return pay;
        }
    }

    /// <summary>
    /// One plain ambient bubble (WPF <c>Bubble</c> with spec == null, FloatUp): the motion, wobble,
    /// breathe and pop-burst math of <c>Bubble.AnimateFrame</c>, one logical step (~30 ms) per
    /// <see cref="Step"/>. Positions are WPF's: DIPs of the bubble's own screen (screen px / scaling),
    /// top-left of the <see cref="Size"/> box. The head only draws and hit-tests.
    /// </summary>
    public sealed class AmbientBubble
    {
        public int Screen;
        public double X, Y, Size, Angle, Scale = 1, Fade = 1;
        public bool Clickable, Popping, Lucky;
        private double _startX, _top, _speed, _wobbleOffset, _timeAlive;
        private int _animType;

        public enum Result { Alive, Missed, Done }

        /// <summary>WPF Bubble ctor for a plain ambient bubble. <paramref name="area"/> is the screen's
        /// working area in px, <paramref name="scaling"/> its DPI scale.</summary>
        public static AmbientBubble Spawn(Random r, int screen, double areaX, double areaY, double areaW, double areaH,
            double scaling, AppSettings s, double? modScale, bool clickable)
        {
            var b = new AmbientBubble { Screen = screen, Clickable = clickable };
            b.Size = BubbleSizing.Scale(r.Next(BubbleSizing.BaseMinDip, BubbleSizing.BaseMaxDip), s.BubblesSize, modScale);
            b._speed = 1.0 + r.NextDouble();   // 1.0 to 2.0 DIP/step
            var boost = s.BubbleSpeedBoost;      // dashboard speed slider, up to +500%
            if (boost > 0) b._speed *= 1.0 + Math.Clamp(boost, 0, 500) / 100.0;
            b._animType = r.Next(4);
            b._wobbleOffset = r.NextDouble() * 100;
            b.Angle = r.Next(360);
            b._top = areaY / scaling - b.Size - 50;
            b.X = b._startX = (areaX + r.Next(50, (int)Math.Max(100, areaW - b.Size - 50))) / scaling;
            b.Y = (areaY + areaH) / scaling;   // start at the bottom
            return b;
        }

        // ---- Bubbles v2 (WPF Bubble ctor + AnimateFrame, the ambientMotion branches) ----

        /// <summary>The concrete travel style of this bubble (never Mix).</summary>
        public BubbleMotionStyle Motion { get; private set; } = BubbleMotionStyle.FloatUp;

        /// <summary>The Brain Drain bubble (WPF spec.VariantId == braindrain_melt): violet, breathes,
        /// dissolves by itself after <see cref="DrainTreatLifeMs"/>, drains the screen when popped.</summary>
        public bool IsDrain { get; private set; }

        /// <summary>The bubble left by itself (treat life ran out): fades like a pop, pays nothing.</summary>
        public bool Dissolving { get; private set; }

        /// <summary>WPF BuildTriggerSpec: SizePx 220, TreatLifeMs 7000.</summary>
        public const int DrainSizeDip = 220;
        public const int DrainTreatLifeMs = 7000;

        private double _bottom, _spiralCx, _spiralCy, _spiralR0, _spiralRadial, _spiralTurns, _spiralDir, _spiralFade = 1;
        private double _lifeMs = -1;
        private SpiralInPath.State _spiral;

        /// <summary>WPF Bubble ctor with an ambient v2 pick. <paramref name="motion"/> is already
        /// resolved (<see cref="AmbientBubbleMotion.Resolve"/>): Rain starts just above the top and
        /// falls, Spiral In starts on a ring around the screen centre and walks inward. The owned
        /// styles run at half speed at MotionLevel.Reduced.</summary>
        public static AmbientBubble Spawn(Random r, int screen, double areaX, double areaY, double areaW, double areaH,
            double scaling, AppSettings s, double? modScale, bool clickable, BubbleMotionStyle motion, MotionLevel level)
        {
            var b = Spawn(r, screen, areaX, areaY, areaW, areaH, scaling, s, modScale, clickable);
            b.ApplyMotion(r, areaX, areaY, areaW, areaH, scaling, motion, level);
            return b;
        }

        /// <summary>WPF BuildTriggerSpec(braindrain_melt) + Bubble ctor: the Brain Drain bubble. It
        /// honours the picked motion like any ambient bubble.</summary>
        public static AmbientBubble SpawnDrain(Random r, int screen, double areaX, double areaY, double areaW, double areaH,
            double scaling, AppSettings s, double? modScale, bool clickable, BubbleMotionStyle motion, MotionLevel level)
        {
            var b = Spawn(r, screen, areaX, areaY, areaW, areaH, scaling, s, modScale, clickable);
            b.IsDrain = true;
            b.Size = DrainSizeDip;
            b._top = areaY / scaling - b.Size - 50;
            b._lifeMs = DrainTreatLifeMs;
            b.ApplyMotion(r, areaX, areaY, areaW, areaH, scaling, motion, level);
            return b;
        }

        private void ApplyMotion(Random r, double areaX, double areaY, double areaW, double areaH,
            double scaling, BubbleMotionStyle motion, MotionLevel level)
        {
            if (motion == BubbleMotionStyle.Mix) motion = BubbleMotionStyle.FloatUp;   // callers resolve; never trust
            Motion = motion;
            _bottom = (areaY + areaH) / scaling + 50;
            if (motion == BubbleMotionStyle.FloatUp) return;
            _speed *= AmbientBubbleMotion.SpeedMult(level);
            if (motion == BubbleMotionStyle.Rain)
            {
                Y = areaY / scaling - Size;   // start just above the top
                return;
            }
            // Spiral In: on a ring around the screen centre (about 42% of the shorter dimension) at
            // a random angle, either direction.
            _spiralCx = (areaX + areaW / 2.0) / scaling;
            _spiralCy = (areaY + areaH / 2.0) / scaling;
            _spiralR0 = SpiralInPath.StartRadius(Math.Min(areaW, areaH) / scaling);
            _spiralRadial = _speed * SpiralInPath.RadialPerSpeed;
            _spiralTurns = SpiralInPath.Turns(level);
            _spiralDir = r.Next(2) == 0 ? 1 : -1;
            _spiral = SpiralInPath.Start(_spiralR0, r.NextDouble() * Math.PI * 2);
            X = _startX = _spiralCx + Math.Cos(_spiral.Angle) * _spiral.Radius - Size / 2.0;
            Y = _spiralCy + Math.Sin(_spiral.Angle) * _spiral.Radius - Size / 2.0;
        }

        /// <summary>What the head draws at: the pop fade, the Spiral In fade over the last band, and
        /// the drain bubble's slow breathe (WPF BrainDrainBubble.PulseAt, live bubbles only).</summary>
        public double DrawOpacity =>
            Math.Clamp(Fade, 0, 1) * _spiralFade * (IsDrain && !Popping ? Chaos.BrainDrainBubble.PulseAt(_timeAlive) : 1.0);

        /// <summary>Draw scale this step: the pop/breathe scale plus WPF's 0.06 x sin(7.5 t) wobble.</summary>
        public double DrawScale => (Scale + 0.06 * Math.Sin(_timeAlive * 7.5 + _wobbleOffset)) * (1.0 + 0.25 * GazeDwell);

        /// <summary>Focus Gaze dwell fill, 0..1 (WPF Bubble.SetGazeDwellProgress: swells to 1.25x).</summary>
        public double GazeDwell;

        public double CenterX => X + Size / 2;
        public double CenterY => Y + Size / 2;

        /// <summary>WPF ContainsPx: inside the hit disc (the sprite's own size for a plain bubble).</summary>
        public bool Contains(double x, double y)
        {
            double dx = x - CenterX, dy = y - CenterY, r = Size / 2;
            return dx * dx + dy * dy <= r * r;
        }

        public Result Step()
        {
            if (Popping)
            {
                // Pop animation - expand and fade; lucky pops linger ~50% longer.
                Scale += 0.04;
                Fade -= Lucky ? 0.044 : 0.066;
                Angle += 2;
                return Fade <= 0 ? Result.Done : Result.Alive;
            }
            _timeAlive += 0.02;
            double offset = 0;
            switch (_animType)
            {
                case 0: offset = Math.Sin(_timeAlive * 6) * 25; Angle = (Angle + 0.34) % 360; break;
                case 1: offset = Math.Sin(_timeAlive * 7.5) * 30; Angle = (Angle + 0.14) % 360; break;
                case 2: offset = Math.Cos(_timeAlive * 5.4) * 25; Angle = (Angle - 0.66) % 360; break;
                case 3: offset = Math.Sin(_timeAlive * 3) * 30 + Math.Cos(_timeAlive * 6) * 15; Angle = (Angle + 0.54) % 360; break;
            }
            // A treat dissolves by itself when its life runs out (WPF TreatLifeMs): no reward, no miss.
            if (_lifeMs > 0 && (_lifeMs -= AmbientBubbleField.StepMs) <= 0)
            {
                Popping = Dissolving = true;
                return Result.Alive;
            }
            switch (Motion)
            {
                case BubbleMotionStyle.SpiralIn:
                    // Angle on, radius in, fade over the last band. Reaching the core is an EXIT like
                    // floating off the top (a miss): never a pop, so no sound, XP or lucky roll.
                    _spiral = SpiralInPath.Step(_spiral, _spiralR0, _spiralRadial, _spiralTurns, _spiralDir, 1.0);
                    X = _spiralCx + Math.Cos(_spiral.Angle) * _spiral.Radius - Size / 2.0;
                    Y = _spiralCy + Math.Sin(_spiral.Angle) * _spiral.Radius - Size / 2.0;
                    _spiralFade = _spiral.Fade;
                    return _spiral.Done ? Result.Missed : Result.Alive;
                case BubbleMotionStyle.Rain:
                    Y += _speed;
                    X = _startX + offset;
                    return Y > _bottom ? Result.Missed : Result.Alive;
            }
            Y -= _speed;
            X = _startX + offset;
            return Y < _top ? Result.Missed : Result.Alive;
        }
    }

    /// <summary>
    /// The plain ambient field of WPF BubbleService: the concurrent cap, the spawn cadence, the
    /// pop reward (lucky roll, daily XP bucket) and one logical step for every bubble. Not a
    /// singleton on purpose: the head owns one per run. UI-thread only.
    /// </summary>
    public sealed class AmbientBubbleField
    {
        /// <summary>WPF MAX_BUBBLES_HOST: the shared-host cap, which is how every bubble renders here.
        /// Solid mode off is WPF's per-window path, capped at 3 (MAX_BUBBLES).</summary>
        public static int Cap(AppSettings s) => s.BubbleSharedHost ? 40 : 3;

        /// <summary>WPF Start/RefreshFrequency: BubblesFrequency is per minute.</summary>
        public static TimeSpan SpawnInterval(int perMinute) => TimeSpan.FromMilliseconds(60000.0 / Math.Max(1, perMinute));

        /// <summary>WPF STEP_MS: the logical step gate (~30 fps).</summary>
        public const double StepMs = 30.0;

        public readonly List<AmbientBubble> Bubbles = new();
        public readonly Random Random = new();

        public bool CanSpawn(AppSettings s) => Bubbles.Count < Cap(s);

        /// <summary>WPF MAX_TRIGGER_WINDOWS: effect bubbles alive at once; past it a spawn is plain.</summary>
        public const int MaxTriggerBubbles = 4;

        /// <summary>WPF RollTriggerSpec, for the one effect bubble this head carries: with effect
        /// bubbles on, the Brain Drain bubble joins the ticked ids as one more equally weighted id
        /// when a v2 style is owned AND its switch is on. True = this spawn is the drain bubble. A
        /// roll that lands on another id spawns a plain bubble here (those variants are not ported),
        /// so the drain bubble's own odds are WPF's.</summary>
        public bool RollDrainBubble(AppSettings s, bool v2Owned)
        {
            if (!s.BubbleTriggersEnabled) return false;
            var ids = Chaos.BrainDrainBubble.RollPool(s.BubbleTriggerVariants, v2Owned, s.BubbleBrainDrainEnabled);
            var count = 0;
            var drainAt = -1;
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == "magnet_pull") continue;   // Pull is retired, including old saved selections
                if (ids[i] == Chaos.BrainDrainBubble.VariantId) drainAt = count;
                count++;
            }
            if (count == 0 || drainAt < 0) return false;
            if (Random.Next(100) >= Math.Clamp(s.BubbleTriggerChance, 0, 100)) return false;
            if (Random.Next(count) != drainAt) return false;
            var live = 0;
            foreach (var b in Bubbles) if (b.IsDrain) live++;
            return live < MaxTriggerBubbles;
        }

        /// <summary>WPF AwardAmbientPop's pure half: roll lucky (5% for 20x with the lucky_bubbles
        /// skill), draw 5 x multiplier from the daily bucket. A lucky roll that paid nothing gets the
        /// ordinary pop. Returns the XP to award; the head plays the sound and credits it.</summary>
        public int Pop(AmbientBubble b, AppSettings s)
        {
            if (b.Popping) return 0;
            b.Popping = true;
            var multiplier = SkillTreeRules.HasSkill(s, "lucky_bubbles") && Random.NextDouble() < 0.05 ? 20 : 1;
            var paid = AmbientBubbleXp.Take(s, 5 * multiplier);
            b.Lucky = multiplier > 1 && paid > 0;
            return paid;
        }

        /// <summary>The topmost live, clickable bubble under a point on one screen, or null (WPF PopTopmostAt).</summary>
        public AmbientBubble? HitTest(int screen, double x, double y)
        {
            for (var i = Bubbles.Count - 1; i >= 0; i--)
            {
                var b = Bubbles[i];
                if (b.Screen == screen && b.Clickable && !b.Popping && b.Contains(x, y)) return b;
            }
            return null;
        }

        /// <summary>One logical step. Missed and finished bubbles are removed; returns the misses.
        /// Allocation-free.</summary>
        public int Step()
        {
            var missed = 0;
            for (var i = Bubbles.Count - 1; i >= 0; i--)
            {
                var r = Bubbles[i].Step();
                if (r == AmbientBubble.Result.Alive) continue;
                if (r == AmbientBubble.Result.Missed) missed++;
                Bubbles.RemoveAt(i);
            }
            return missed;
        }
    }

    /// <summary>The Bubble Pop control seam (WPF App.Bubbles): the engine and the card start,
    /// stop and re-time it; the head owns the surface. Unseeded = silent no-op.</summary>
    public static class CoreBubbles
    {
        public static volatile Action? StartAction;
        public static volatile Action? StopAction;
        public static volatile Action? RefreshFrequencyAction;

        public static void Start() { try { StartAction?.Invoke(); } catch { } }
        public static void Stop() { try { StopAction?.Invoke(); } catch { } }
        public static void RefreshFrequency() { try { RefreshFrequencyAction?.Invoke(); } catch { } }
        /// <summary>WPF BubbleService.PauseAndClear / Resume: a minigame (bubble count) holds the field.
        /// Resume restarts only what Pause stopped, and never after a Stop.</summary>
        public static volatile Action? PauseAction, ResumeAction;
        public static void Pause() { try { PauseAction?.Invoke(); } catch { } }
        public static void Resume() { try { ResumeAction?.Invoke(); } catch { } }
    }
}
