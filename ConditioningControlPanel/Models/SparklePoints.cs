using System;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// The one place the Sparkle Points (skill points) ceiling lives on the client.
    ///
    /// The server clamps <c>skill_points</c> to <c>SKILL_POINTS_CAP</c> on every write path; this
    /// constant must match it. It was 9,999 and rose to 99,999 with the Back Room (CONTRACT.md 3.4),
    /// so a slot win can take a balance past four digits. Every client write goes through
    /// <see cref="AppSettings.SkillPoints"/>, which clamps with <see cref="Clamp"/>, so earn paths,
    /// sync merges, restores and purchase responses all agree on the same ceiling.
    /// </summary>
    public static class SparklePoints
    {
        /// <summary>Highest balance an account can hold. Matches server SKILL_POINTS_CAP.</summary>
        public const int Cap = 99_999;

        /// <summary>Clamp a balance into [0, <see cref="Cap"/>].</summary>
        public static int Clamp(int value) => Math.Clamp(value, 0, Cap);

        /// <summary>
        /// Sync merge rule: the balance only rises outside a purchase or an admin reset, so the
        /// higher of server and local wins, clamped to the cap.
        /// </summary>
        public static int MergeMax(int server, int local) => Clamp(Math.Max(server, local));

        /// <summary>
        /// A skill purchase the server refused for balance. The client credits level-ups and bubble
        /// milestones the moment they happen, but the server only credits them at its next sync and
        /// counts bubbles from the season's start, so its 100-bubble boundaries fall on different
        /// pops than ours (ccp-bugs #1268 #1269). While ours runs ahead, the wallet shows a point the
        /// purchase will not honour. Returns the server's number when it refused for balance and is
        /// below ours, so every surface shows what a purchase is judged on; null keeps local.
        /// Nothing is lost: the server credits the same point at its own boundary and MergeMax
        /// raises us back on that sync.
        /// </summary>
        public static int? AdoptAfterRefusal(int local, int? server, int cost)
        {
            if (!server.HasValue) return null;
            var s = Clamp(server.Value);
            if (s >= cost || s >= local) return null;
            return s;
        }

        /// <summary>
        /// What a balance refusal does next (ccp-bugs #1300). The server credits our pending
        /// level-ups and bubble milestones only when a sync reaches it, and for a Back Room account
        /// the purchase route will not take our number either, so a tree that reads 10 can be
        /// refused at 9 on the first click. Before lowering the wallet we sync once and ask again:
        /// the point we show is usually one the server is about to credit. Only a refusal that
        /// survives that sync adopts the server's number.
        /// </summary>
        public static RefusalStep AfterBalanceRefusal(int local, int? server, int cost, bool alreadySynced)
        {
            if (!AdoptAfterRefusal(local, server, cost).HasValue) return RefusalStep.Keep;
            return alreadySynced ? RefusalStep.Adopt : RefusalStep.SyncAndRetry;
        }

        public enum RefusalStep { Keep, SyncAndRetry, Adopt }
    }
}
