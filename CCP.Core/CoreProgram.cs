using System;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// What the Training Programs ledger asks of whichever head is hosting it. The schedule, the
    /// enrollment ledger and the progress rules live in Core (Services/Program/ProgramService.cs);
    /// the five things below cannot, because each is a toast, a badge store, a content library or
    /// a pledge check that only a head owns.
    ///
    /// Unseeded, every member answers - and answers the safe way:
    ///  - <see cref="HasPremium"/> is FALSE, so a premium program refuses to enroll and a premium
    ///    task stops blocking the day. Both are the service's existing lapsed-pledge paths.
    ///  - <see cref="Notify"/> and <see cref="UnlockAchievement"/> are no-ops. A missed toast is a
    ///    quieter app; a missed badge is re-granted the next time the head is present.
    ///  - <see cref="ActivePackVideoCount"/> is 0, which only ever removes a reason to believe the
    ///    video library is stocked - the folder probe still decides, and "unknown" already resolves
    ///    to not-blocked.
    ///  - <see cref="Roadmap"/> is null, which the ritual-photo path already handles by filing
    ///    nothing and logging it. A program day never hinges on the photo landing.
    /// </summary>
    public static class CoreProgram
    {
        /// <summary>The user's pledge is current. Unseeded: false.</summary>
        public static volatile Func<bool>? HasPremiumProvider;

        /// <summary>Message, <c>NotificationType</c> member name, duration. Unseeded: no toast.</summary>
        public static volatile Action<string, string, TimeSpan>? NotifyProvider;

        /// <summary>Achievement id. Unseeded: nothing unlocks.</summary>
        public static volatile Action<string>? UnlockAchievementProvider;

        /// <summary>Videos supplied by the active content packs. Unseeded: 0.</summary>
        public static volatile Func<int>? ActivePackVideoCountProvider;

        /// <summary>The head's roadmap instance, for filing ritual photos. Unseeded: null.</summary>
        public static volatile Func<RoadmapService?>? RoadmapProvider;

        public static bool HasPremium
        {
            get { try { return HasPremiumProvider?.Invoke() == true; } catch { return false; } }
        }

        public static void Notify(string message, string kind, TimeSpan duration)
        {
            try { NotifyProvider?.Invoke(message, kind, duration); } catch { }
        }

        public static void UnlockAchievement(string achievementId)
        {
            try { UnlockAchievementProvider?.Invoke(achievementId); }
            catch (Exception ex) { Log.Debug("CoreProgram: unlocking '{Id}' failed: {E}", achievementId, ex.Message); }
        }

        public static int ActivePackVideoCount()
        {
            try { return Math.Max(0, ActivePackVideoCountProvider?.Invoke() ?? 0); } catch { return 0; }
        }

        public static RoadmapService? Roadmap()
        {
            try { return RoadmapProvider?.Invoke(); } catch { return null; }
        }
    }
}
