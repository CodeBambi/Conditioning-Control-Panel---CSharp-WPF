using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>Where a billboard card leads when it is clicked.</summary>
    public enum BillboardTargetKind
    {
        /// <summary>An absolute https address, opened in the user's own browser.</summary>
        Link,

        /// <summary>A <c>ShowTab</c> key, opened in this window.</summary>
        Tab,
    }

    /// <summary>How a card's art is painted, which depends entirely on the shape of the file.</summary>
    public enum BillboardArt
    {
        /// <summary>A 16:9 poster. Cover-fitted. Every card in the house roster is one of these.</summary>
        Cover,

        /// <summary>
        /// A square icon or a logo. Cover-fitting one of these blows a 512px mark up to fill a
        /// 16:9 card and it reads as a blurry giant - which is exactly what the first desk pass
        /// found - so these get a plate instead: the same image blurred and darkened as a
        /// backdrop, with the mark at 72px in the middle.
        ///
        /// <para>No house card uses this any more, now that all six have proper posters. It stays
        /// for the next card that arrives with nothing but an icon, which is the situation that
        /// produced the bug in the first place.</para>
        /// </summary>
        Plate,
    }

    /// <summary>
    /// One card on the dashboard billboard: art with a shade, a mono eyebrow, a title, one line
    /// and a chevron. Modelled on the doors on the CC Labs remix page (<c>remix/ui/doors.js</c>)
    /// rather than ported from them.
    /// </summary>
    /// <param name="Id">Stable key, used in logs and tests. Never shown.</param>
    /// <param name="EyebrowKey">Localisation key for the small mono line above the title.</param>
    /// <param name="TitleKey">Localisation key for the title.</param>
    /// <param name="LineKey">Localisation key for the one line under the title.</param>
    /// <param name="Poster">Resource-relative art path, e.g. <c>features/loom.png</c>.</param>
    /// <param name="Art">Cover for wide feature art, Plate for a square mark.</param>
    /// <param name="Kind">Link or Tab.</param>
    /// <param name="Target">An https address, or a ShowTab key.</param>
    public sealed record BillboardCard(
        string Id,
        string EyebrowKey,
        string TitleKey,
        string LineKey,
        string Poster,
        BillboardArt Art,
        BillboardTargetKind Kind,
        string Target);

    /// <summary>
    /// Where the rack stands: which roster card is in each slot, which card goes in next, which
    /// slot it will replace, and which slot changed on the step that produced this state
    /// (<c>-1</c> when nothing moved, so the paint knows there is nothing to fade).
    /// </summary>
    public sealed record BillboardRack(IReadOnlyList<int> Slots, int NextCard, int NextSlot, int ChangedSlot);

    /// <summary>
    /// The rack that takes the space when the dashboard's browser card is folded shut (owner ask
    /// 2026-09-12, reshaped after the first desk pass: one huge card blew a square logo up into a
    /// blurry giant, so it is a 2x2 grid of small cards now).
    ///
    /// <para>Four cards on screen at once out of a roster of six, and every twelve seconds ONE
    /// slot swaps to the card that has been off screen longest. Same manners as the doors it is
    /// modelled on: nothing opens by itself, nothing blocks, the pointer on the rack stops the
    /// clock, and the dashboard works exactly the same with the whole thing ignored.</para>
    ///
    /// <para>Everything here is pure so the roster and the walk are unit tested without a window.
    /// <c>MainWindow.DashboardBillboard.cs</c> owns the cards, the timer and the paint.</para>
    /// </summary>
    public static class DashboardBillboard
    {
        /// <summary>How long a slot holds before the rack swaps it.</summary>
        public const int RotateSeconds = 12;

        /// <summary>Cards on screen at once: a 2x2 grid.</summary>
        public const int RackSlots = 4;

        /// <summary>Where a poster's pack URI is rooted.</summary>
        private const string PackRoot = "pack://application:,,,/Resources/";

        /// <summary>The support page the rest of the app already links to.</summary>
        public const string PatreonUrl = "https://www.patreon.com/CodeBambi";

        /// <summary>
        /// The house roster. Art the repo already ships, and addresses the app already knows: the
        /// Discord invite and the Patreon page come from the constants the rest of the app uses, so
        /// a server or campaign move is one edit somewhere else and this follows. Every card lands
        /// where its title says it will: the community card opens the server invite rather than
        /// the in-app Discord profile page, and a feature card opens that feature's own page.
        /// External cards on cclabs.app carry <c>from=panel</c> so the visit can be counted where
        /// it lands, exactly as the remix doors do.
        ///
        /// <para>Every card has its own poster in <c>Resources/billboard/</c> (2026-09-12): 688x384,
        /// 16:9, no text, dark and calm across the bottom third so the shade and the words sit on
        /// ground that was drawn for them. That is why all six are <see cref="BillboardArt.Cover"/>
        /// and none of them needs the plate. Poster paths stay data, so a repaint is one string
        /// here and the file beside it.</para>
        /// </summary>
        private static readonly BillboardCard[] Cards =
        {
            new("webapp", "billboard_webapp_eyebrow", "billboard_webapp_title", "billboard_webapp_line",
                "billboard/webapp.png", BillboardArt.Cover,
                BillboardTargetKind.Link, "https://app.cclabs.app/?from=panel"),

            new("remix", "billboard_remix_eyebrow", "billboard_remix_title", "billboard_remix_line",
                "billboard/remix.png", BillboardArt.Cover,
                BillboardTargetKind.Link, "https://cclabs.app/remix/?from=panel"),

            new("loom", "billboard_loom_eyebrow", "billboard_loom_title", "billboard_loom_line",
                "billboard/loom.png", BillboardArt.Cover,
                BillboardTargetKind.Link, "https://cclabs.app/loom/?from=panel"),

            // The server invite, not ShowTab("discord"): that key lands on the in-app Discord
            // PROFILE page, and a card that says "join the community" has to open the front door.
            // DiscordLinks.Invite is the same constant the account shell's Discord button uses.
            new("discord", "billboard_discord_eyebrow", "billboard_discord_title", "billboard_discord_line",
                "billboard/discord.png", BillboardArt.Cover,
                BillboardTargetKind.Link, DiscordLinks.Invite),

            new("exclusives", "billboard_exclusives_eyebrow", "billboard_exclusives_title", "billboard_exclusives_line",
                "billboard/exclusives.png", BillboardArt.Cover,
                BillboardTargetKind.Tab, "exclusives"),

            // Patreon, not the linktr.ee hub (owner call): the page the app already sends people
            // to from the Patreon panel and the remote-control upsell.
            new("support", "billboard_support_eyebrow", "billboard_support_title", "billboard_support_line",
                "billboard/support.png", BillboardArt.Cover,
                BillboardTargetKind.Link, PatreonUrl),
        };

        /// <summary>The roster, in the order the rack walks it.</summary>
        public static IReadOnlyList<BillboardCard> Roster => Cards;

        /// <summary>The pack URI for a card's art.</summary>
        public static string PosterUri(BillboardCard card) => PackRoot + card.Poster;

        /// <summary>The card at an index, or the first one when the index has drifted.</summary>
        public static BillboardCard CardAt(int index) => Cards[ClampIndex(index, Cards.Length)];

        /// <summary>An index that is always inside the roster, whatever it was.</summary>
        public static int ClampIndex(int index, int count)
        {
            if (count <= 0) return 0;
            if (index < 0 || index >= count) return 0;
            return index;
        }

        /// <summary>
        /// The rack as it opens: the first cards in roster order, and the pointer parked on the
        /// first one that did not fit. <c>ChangedSlot</c> is -1 because nothing has moved yet.
        /// </summary>
        public static BillboardRack InitialRack(int rosterCount, int slots = RackSlots)
        {
            int count = Math.Max(0, rosterCount);
            int width = Math.Max(0, Math.Min(slots, count));
            var filled = new int[width];
            for (int i = 0; i < width; i++) filled[i] = i;
            int next = count == 0 ? 0 : width % count;
            return new BillboardRack(filled, next, 0, -1);
        }

        /// <summary>
        /// One step: the card that has been off screen longest takes the slot whose card has been
        /// up longest. Returns the rack unchanged with <c>ChangedSlot = -1</c> when the roster is
        /// not bigger than the rack, because then there is nothing off screen to bring on.
        ///
        /// <para>The incoming card can never already be on screen: cards and slots advance in
        /// lockstep, so every resident was dealt between one and <c>Slots.Count</c> steps ago and
        /// the card pointer has moved by that same amount, which is smaller than the roster.</para>
        /// </summary>
        public static BillboardRack NextRack(BillboardRack current, int rosterCount)
        {
            if (current == null || current.Slots.Count == 0) return InitialRack(rosterCount);
            if (rosterCount <= current.Slots.Count) return current with { ChangedSlot = -1 };

            var slots = new int[current.Slots.Count];
            for (int i = 0; i < slots.Length; i++) slots[i] = current.Slots[i];

            int slot = ClampSlot(current.NextSlot, slots.Length);
            slots[slot] = ClampIndex(current.NextCard, rosterCount);

            return new BillboardRack(slots, (slots[slot] + 1) % rosterCount, (slot + 1) % slots.Length, slot);
        }

        private static int ClampSlot(int slot, int width)
        {
            if (width <= 0) return 0;
            if (slot < 0 || slot >= width) return 0;
            return slot;
        }

        /// <summary>
        /// Whether the clock may tick on. The rack holds still while the pointer is on it - a card
        /// that walks away mid-read is a card nobody finishes - and while the dashboard is not the
        /// tab on screen, where a timer would only be spending frames.
        /// </summary>
        public static bool ShouldAdvance(bool pointerOver, bool onScreen) => onScreen && !pointerOver;
    }
}
