using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    // The pre-7.1 Home slideshow: six house cards, one at a time, a dot per card. WPF replaced it
    // with the Tonight Board deck in 7.1.0 (main 35efcf30e) and main deleted these members from
    // DashboardBillboard. The Avalonia head still shows this slideshow until the deck is ported
    // (main-sync-6 lane sync6-tonight-board); delete this file in that lane. The members are the
    // ones DashboardBillboard carried at 0d9e83383, renamed so they cannot collide with the
    // Tonight Board's BillboardArt.

    /// <summary>What a slide's button does.</summary>
    public enum SlideTarget { Link, Tab }

    /// <summary>How the slide's art is drawn: full cover, or a plate inside the card.</summary>
    public enum SlideArt { Cover, Plate }

    /// <summary>One slide. <paramref name="Poster"/> is a path under the shipped Resources/Assets tree.</summary>
    public sealed record SlideCard(
        string Id,
        string EyebrowKey,
        string TitleKey,
        string LineKey,
        string Poster,
        SlideArt Art,
        SlideTarget Kind,
        string Target);

    public static class LegacyBillboard
    {
        /// <summary>Seconds a slide holds before the next one.</summary>
        public const int RotateSeconds = 12;

        /// <summary>The support page the rest of the app already links to.</summary>
        public const string PatreonUrl = "https://www.patreon.com/CodeBambi";

        private static readonly SlideCard[] Cards =
        {
            new("webapp", "billboard_webapp_eyebrow", "billboard_webapp_title", "billboard_webapp_line",
                "billboard/webapp.png", SlideArt.Cover, SlideTarget.Link, "https://app.cclabs.app/?from=panel"),
            new("remix", "billboard_remix_eyebrow", "billboard_remix_title", "billboard_remix_line",
                "billboard/remix.png", SlideArt.Cover, SlideTarget.Link, "https://cclabs.app/remix/?from=panel"),
            new("loom", "billboard_loom_eyebrow", "billboard_loom_title", "billboard_loom_line",
                "billboard/loom.png", SlideArt.Cover, SlideTarget.Link, "https://cclabs.app/loom/?from=panel"),
            // The server invite: a card that says "join the community" opens the front door.
            new("discord", "billboard_discord_eyebrow", "billboard_discord_title", "billboard_discord_line",
                "billboard/discord.png", SlideArt.Cover, SlideTarget.Link, DiscordLinks.Invite),
            new("exclusives", "billboard_exclusives_eyebrow", "billboard_exclusives_title", "billboard_exclusives_line",
                "billboard/exclusives.png", SlideArt.Cover, SlideTarget.Tab, "exclusives"),
            new("support", "billboard_support_eyebrow", "billboard_support_title", "billboard_support_line",
                "billboard/support.png", SlideArt.Cover, SlideTarget.Link, PatreonUrl),
        };

        /// <summary>The roster, in the order the slideshow walks it.</summary>
        public static IReadOnlyList<SlideCard> Roster => Cards;

        /// <summary>The card at an index, or the first one when the index has drifted.</summary>
        public static SlideCard CardAt(int index) => Cards[index < 0 || index >= Cards.Length ? 0 : index];

        /// <summary>The slideshow turns only while on screen and not hovered.</summary>
        public static bool ShouldAdvance(bool pointerOver, bool onScreen) => onScreen && !pointerOver;
    }
}
