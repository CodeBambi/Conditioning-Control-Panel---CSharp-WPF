using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConditioningControlPanel.Services.Prizes
{
    /// <summary>
    /// The Back Room prize counter's grant ids (owner redesign, 2026-09-13). These strings are a
    /// wire contract with the server, which is the only authority on who owns what: the client
    /// only ever asks <see cref="OwnershipService.IsGranted"/> about them. Appending is safe;
    /// renaming one strands every account that already owns it.
    /// </summary>
    public static class PrizeGrants
    {
        /// <summary>The Jackpot Remix flash.</summary>
        public const string JackpotRemix = "fx.jackpot_remix";

        /// <summary>Flashes v2: the drift-and-bounce motion.</summary>
        public const string FlashDriftBounce = "fx.flash.drift_bounce";

        /// <summary>Flashes v2: the pendulum motion.</summary>
        public const string FlashPendulum = "fx.flash.pendulum";

        /// <summary>Bubbles v2: rain.</summary>
        public const string BubbleRain = "fx.bubble.rain";

        /// <summary>Bubbles v2: spiral in.</summary>
        public const string BubbleSpiralIn = "fx.bubble.spiral_in";

        /// <summary>Prefix of the Racing Thoughts original tracks; see <see cref="RacingTrack"/>.</summary>
        public const string RacingTrackPrefix = "rt.original.";

        /// <summary>Highest Racing Thoughts original track number with a grant.</summary>
        public const int RacingTrackMax = 10;

        /// <summary>
        /// Server-only: the Discord High Roller role. Listed so the id has one spelling in the
        /// codebase, but the client NEVER gates anything on it and it is not in <see cref="All"/>.
        /// </summary>
        public const string DiscordHighRoller = "discord.high_roller";

        /// <summary>The 16 grants the client gates on, in roster order.</summary>
        public static readonly IReadOnlyList<string> All = BuildAll();

        /// <summary>
        /// "rt.original.NN" for Racing Thoughts original track <paramref name="trackNum"/>
        /// (0 to <see cref="RacingTrackMax"/>, always two digits).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Outside 0 to 10.</exception>
        public static string RacingTrack(int trackNum)
        {
            if (trackNum < 0 || trackNum > RacingTrackMax)
                throw new ArgumentOutOfRangeException(nameof(trackNum), trackNum, "Racing Thoughts original tracks run 0 to 10.");
            return RacingTrackPrefix + trackNum.ToString("00", CultureInfo.InvariantCulture);
        }

        private static IReadOnlyList<string> BuildAll()
        {
            var all = new List<string> { JackpotRemix, FlashDriftBounce, FlashPendulum, BubbleRain, BubbleSpiralIn };
            for (var i = 0; i <= RacingTrackMax; i++) all.Add(RacingTrack(i));
            return all.AsReadOnly();
        }
    }
}
