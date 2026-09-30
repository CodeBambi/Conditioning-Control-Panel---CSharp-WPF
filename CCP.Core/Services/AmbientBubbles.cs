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

        /// <summary>Draw scale this step: the pop/breathe scale plus WPF's 0.06 x sin(7.5 t) wobble.</summary>
        public double DrawScale => Scale + 0.06 * Math.Sin(_timeAlive * 7.5 + _wobbleOffset);

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
    }
}
