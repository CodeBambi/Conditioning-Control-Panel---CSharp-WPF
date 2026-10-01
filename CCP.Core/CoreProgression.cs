using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The progression seam: the two things ported views ask of the XP and achievement services.
    /// <c>ProgressionService</c> and <c>AchievementService</c> stay in the WPF head - they own the
    /// companion bonuses, the level-up popups and the achievement toast windows, none of which is
    /// engine work.
    ///
    /// <para>Unseeded means "this head has no progression", which is a real state (the Avalonia
    /// head today), not an error: XP is silently not awarded, the streak is silently not recorded,
    /// nothing throws, and a view that awards XP still finishes its own flow. That is the whole
    /// contract - both calls are fire-and-forget on the WPF side too, where the services are
    /// reached through a null-conditional <c>App.Progression?.</c>.</para>
    ///
    /// <para><b>Why <c>source</c> is a string.</b> <c>XPSource</c> is a plain enum with no head
    /// dependencies, but it is declared inside
    /// <c>ConditioningControlPanel/Services/Companion/CompanionService.cs</c>, a file that uses
    /// <c>System.Windows.Threading</c> and <c>App.</c> and so cannot move. Declaring a second copy
    /// here is exactly the drift this repo forbids, so the seam names the member and the head
    /// parses it. The enum's own comment - members are APPENDED, never reordered - is what makes a
    /// name-based parse stable.
    /// ponytail: string until XPSource is extracted out of CompanionService.cs into Core; then
    /// this parameter becomes the enum and the head's Enum.TryParse goes away.</para>
    ///
    /// <para>Volatile for the same reason as <see cref="CoreMods"/>: a head seeds these on the
    /// startup thread while a view may award XP from a timer or background thread that never
    /// triggers the head's type initializer.</para>
    /// </summary>
    public static class CoreProgression
    {
        /// <summary>Amount and <c>XPSource</c> member name. Null when the head has no XP service.</summary>
        public static volatile Action<double, string>? AddXPProvider;

        /// <summary>True on a correct bubble-count answer, false on a wrong one (breaks the streak).</summary>
        public static volatile Action<bool>? TrackBubbleCountResultProvider;

        /// <summary>
        /// Awards XP. <paramref name="source"/> is an <c>XPSource</c> member name; an unknown name
        /// is charged to <c>Other</c> by the head rather than dropped. Swallows provider faults - a
        /// throwing progression service must never take the awarding view down with it.
        /// </summary>
        /// <returns>False only when the provider threw (no provider is not a failure).</returns>
        public static bool AddXP(double amount, string source = "Other")
        {
            try { AddXPProvider?.Invoke(amount, source); return true; } catch { return false; }
        }

        /// <summary>
        /// Records a bubble-count answer against the achievement streak. Faults are swallowed -
        /// see <see cref="AddXP"/>.
        /// </summary>
        public static void TrackBubbleCountResult(bool correct)
        {
            try { TrackBubbleCountResultProvider?.Invoke(correct); } catch { }
        }

        /// <summary>Seconds of a mandatory video watched (WPF AchievementService.TrackVideoWatched).</summary>
        public static volatile Action<double>? TrackVideoWatchedProvider;

        public static void TrackVideoWatched(double seconds)
        {
            try { TrackVideoWatchedProvider?.Invoke(seconds); } catch { }
        }

        /// <summary>WPF AchievementService.TrackBubbleCountGameStarted.</summary>
        public static volatile Action? TrackBubbleCountGameStartedProvider;
        /// <summary>WPF QuestService.TrackBubbleCountCompleted.</summary>
        public static volatile Action? TrackBubbleCountCompletedProvider;

        public static void TrackBubbleCountGameStarted() { try { TrackBubbleCountGameStartedProvider?.Invoke(); } catch { } }
        public static void TrackBubbleCountCompleted() { try { TrackBubbleCountCompletedProvider?.Invoke(); } catch { } }

        /// <summary>WPF App.Quests?.TrackMantraCompleted(), for <c>MantraService</c>.</summary>
        public static volatile Action? TrackMantraCompletedProvider;
        public static void TrackMantraCompleted() { try { TrackMantraCompletedProvider?.Invoke(); } catch { } }

        /// <summary>A mandatory video's attention checks passed (true) or failed (false).</summary>
        public static volatile Action<bool>? TrackAttentionCheckProvider;

        public static void TrackAttentionCheck(bool passed)
        {
            try { TrackAttentionCheckProvider?.Invoke(passed); } catch { }
        }
    }
}
