using System;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// The weekly free Super preview, as pure maths. A week runs Monday 00:00 UTC to the next
    /// Monday 00:00 UTC (UTC on purpose: no daylight saving, no local clock, the same week for
    /// every player). Each week lends ONE effect, picked off the <see cref="SuperEffect"/> wheel
    /// in enum order, for one 10 second try per account. No WPF, no App: the service and the
    /// switch only read these answers.
    /// </summary>
    public static class SuperPreviewRule
    {
        /// <summary>Length of the one try, seconds.</summary>
        public const int TrySeconds = 10;

        /// <summary>How long a natural end fades out, ms. A panic end is a cut, never a fade.</summary>
        public const int FadeOutMs = 600;

        /// <summary>"Never used" in <c>AppSettings.SuperPreviewUsedWeek</c>.</summary>
        public const int NeverUsed = -1;

        /// <summary>Week zero: Monday 1970-01-05 00:00 UTC, the first Monday after the Unix epoch.</summary>
        public static readonly DateTimeOffset EpochMonday = new(1970, 1, 5, 0, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Wheel offset so the week starting Monday 2026-10-05 lands on <see cref="SuperEffect.Vortex"/>
        /// (owner's launch pick). Week 2961 mod 8 = 1, plus 2 = 3 = Vortex.
        /// </summary>
        private const int WheelOffset = 2;

        private static readonly int WheelSize = Enum.GetValues(typeof(SuperEffect)).Length;

        /// <summary>Whole weeks since <see cref="EpochMonday"/>, floored (negative before it).</summary>
        public static int WeekIndex(DateTimeOffset now)
        {
            var days = (now.UtcDateTime - EpochMonday.UtcDateTime).TotalDays;
            return (int)Math.Floor(days / 7.0);
        }

        /// <summary>Monday 00:00 UTC that opens week <paramref name="weekIndex"/>.</summary>
        public static DateTimeOffset WeekStart(int weekIndex) => EpochMonday.AddDays(weekIndex * 7.0);

        /// <summary>When this week's preview swaps for the next one.</summary>
        public static DateTimeOffset NextSwap(DateTimeOffset now) => WeekStart(WeekIndex(now) + 1);

        /// <summary>The effect lent out in week <paramref name="weekIndex"/>.</summary>
        public static SuperEffect EffectForWeek(int weekIndex)
        {
            int slot = ((weekIndex + WheelOffset) % WheelSize + WheelSize) % WheelSize;
            return (SuperEffect)slot;
        }

        /// <summary>This week's free effect.</summary>
        public static SuperEffect EffectThisWeek(DateTimeOffset now) => EffectForWeek(WeekIndex(now));

        /// <summary>
        /// May this account start a try of <paramref name="effect"/> now? Only this week's effect,
        /// only once a week (<paramref name="usedWeek"/> is the week index of the last try).
        /// </summary>
        public static bool CanTry(SuperEffect effect, int usedWeek, DateTimeOffset now)
        {
            int week = WeekIndex(now);
            return effect == EffectForWeek(week) && usedWeek != week;
        }

        /// <summary>The try for this week is already spent.</summary>
        public static bool UsedThisWeek(int usedWeek, DateTimeOffset now) => usedWeek == WeekIndex(now);

        /// <summary>
        /// The Super gate in one line, so the truth table is testable without App:
        /// unlocked = paid tier, or the effect is the one being tried right now.
        /// </summary>
        public static bool IsUnlocked(bool hasTier, SuperEffect effect, SuperEffect? trying)
            => hasTier || trying == effect;

        /// <summary>
        /// On = unlocked AND (switched on, or being tried). A try is on by definition: it is the
        /// only way a free player sees the effect.
        /// </summary>
        public static bool IsOn(bool hasTier, bool switchedOn, SuperEffect effect, SuperEffect? trying)
            => trying == effect || (switchedOn && IsUnlocked(hasTier, effect, trying));

        /// <summary>Seconds left in a try, clamped to [0, TrySeconds].</summary>
        public static double SecondsLeft(DateTimeOffset startedAt, DateTimeOffset now)
        {
            var left = TrySeconds - (now - startedAt).TotalSeconds;
            return Math.Clamp(left, 0, TrySeconds);
        }
    }
}
