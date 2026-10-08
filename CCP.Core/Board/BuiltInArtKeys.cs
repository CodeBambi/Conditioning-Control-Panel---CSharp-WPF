using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Billboard
{
    /// <summary>The art keys the deck lane registers itself. Providers pick one of these (or a
    /// key another lane registers: "board", "clip").</summary>
    public static class BuiltInArtKeys
    {
        /// <summary>A 16:9 poster. ArtData = a resource path under Resources/, e.g. "billboard/loom.png".</summary>
        public const string Poster = "poster";

        /// <summary>Open tables sliding in. ArtData = optional {count} (IReadOnlyDictionary of string to int, 1..3 rows drawn) or names.</summary>
        public const string Tables = "tables";

        /// <summary>A prize wheel turning. ArtData = optional {done, total}: a pip per slot under it.</summary>
        public const string Wheel = "wheel";

        /// <summary>A four-arm spiral in the card hue. ArtData = optional second hue "#rrggbb", or null.</summary>
        public const string Spiral = "spiral";

        /// <summary>A month of days. ArtData = {counted, need, days, today} (Locktober), {day, days} (a program), or null.</summary>
        public const string Calendar = "calendar";

        /// <summary>Four tiles, one switching on and off under a click ring. ArtData = the tip id (unused).</summary>
        public const string Tip = "tip";

        /// <summary>A paper invite pass: a code row with one glyph flipping up, seven day pips on the stub. ArtData unused.</summary>
        public const string Invite = "invite";

        /// <summary>Today's quests as slips on a clipboard, the done ones checked. ArtData = {done, total}.</summary>
        public const string Quests = "quests";

        public static readonly IReadOnlyList<string> All = new[] { Poster, Tables, Wheel, Spiral, Calendar, Tip, Invite, Quests };
    }
}
