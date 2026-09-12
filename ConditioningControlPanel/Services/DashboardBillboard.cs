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

    /// <summary>
    /// One card on the dashboard billboard: a poster with a shade, a mono eyebrow, a title, one
    /// line and a chevron. Modelled on the doors on the CC Labs remix page
    /// (<c>remix/ui/doors.js</c>) rather than ported from them.
    /// </summary>
    /// <param name="Id">Stable key, used in logs and tests. Never shown.</param>
    /// <param name="EyebrowKey">Localisation key for the small mono line above the title.</param>
    /// <param name="TitleKey">Localisation key for the title.</param>
    /// <param name="LineKey">Localisation key for the one line under the title.</param>
    /// <param name="Poster">Resource-relative art path, e.g. <c>features/loom.png</c>.</param>
    /// <param name="Kind">Link or Tab.</param>
    /// <param name="Target">An https address, or a ShowTab key.</param>
    public sealed record BillboardCard(
        string Id,
        string EyebrowKey,
        string TitleKey,
        string LineKey,
        string Poster,
        BillboardTargetKind Kind,
        string Target);

    /// <summary>
    /// The rotating strip that takes the space when the dashboard's browser card is folded shut
    /// (owner ask 2026-09-12). One card at a time, dots for the roster, the next card on a timer.
    ///
    /// <para>Same manners as the doors it is modelled on: nothing opens by itself, nothing blocks,
    /// and the dashboard works exactly the same with the whole strip ignored. The mouse over it
    /// stops the clock, a dot jumps, and a click is the only thing that ever leaves the page.</para>
    ///
    /// <para>Everything here is pure so the roster and the walk are unit tested without a window.
    /// <c>MainWindow.DashboardBillboard.cs</c> owns the card, the timer and the paint.</para>
    /// </summary>
    public static class DashboardBillboard
    {
        /// <summary>How long a card holds before the strip walks to the next one.</summary>
        public const int RotateSeconds = 12;

        /// <summary>Where a poster's pack URI is rooted.</summary>
        private const string PackRoot = "pack://application:,,,/Resources/";

        /// <summary>
        /// The house roster. Art the repo already ships, and addresses the app already uses: the
        /// linktr.ee address is the one the header hint has always pointed at, and the two Tab
        /// cards hand off to pages this window owns rather than sending anyone to a browser for
        /// something that is already here. External cards carry <c>from=panel</c> so the visit can
        /// be counted where it lands, exactly as the remix doors do.
        /// </summary>
        private static readonly BillboardCard[] Cards =
        {
            new("webapp", "billboard_webapp_eyebrow", "billboard_webapp_title", "billboard_webapp_line",
                "features/remote_control.png", BillboardTargetKind.Link, "https://app.cclabs.app/?from=panel"),

            new("remix", "billboard_remix_eyebrow", "billboard_remix_title", "billboard_remix_line",
                "features/corner_gif.png", BillboardTargetKind.Link, "https://cclabs.app/remix/?from=panel"),

            new("loom", "billboard_loom_eyebrow", "billboard_loom_title", "billboard_loom_line",
                "features/loom.png", BillboardTargetKind.Link, "https://cclabs.app/loom/?from=panel"),

            new("discord", "billboard_discord_eyebrow", "billboard_discord_title", "billboard_discord_line",
                "discord.png", BillboardTargetKind.Tab, "discord"),

            new("exclusives", "billboard_exclusives_eyebrow", "billboard_exclusives_title", "billboard_exclusives_line",
                "features/fyp.png", BillboardTargetKind.Tab, "exclusives"),

            new("support", "billboard_support_eyebrow", "billboard_support_title", "billboard_support_line",
                "logo.png", BillboardTargetKind.Link, "https://linktr.ee/CodeBambi"),
        };

        /// <summary>The roster, in the order the strip walks it.</summary>
        public static IReadOnlyList<BillboardCard> Roster => Cards;

        /// <summary>The pack URI for a card's poster.</summary>
        public static string PosterUri(BillboardCard card) => PackRoot + card.Poster;

        /// <summary>
        /// The walk: one step forward, wrapping at the end. An out-of-range index (a roster that
        /// shrank under a running timer) comes back to the first card rather than throwing.
        /// </summary>
        public static int NextIndex(int current, int count)
        {
            if (count <= 0) return 0;
            if (current < 0 || current >= count) return 0;
            return (current + 1) % count;
        }

        /// <summary>A dot click. Out of range lands on the first card, never off the end.</summary>
        public static int JumpTo(int requested, int count)
        {
            if (count <= 0) return 0;
            if (requested < 0 || requested >= count) return 0;
            return requested;
        }

        /// <summary>
        /// Whether the clock may tick on. The strip holds still while the pointer is on it - a
        /// card that walks away mid-read is a card nobody finishes - and while the dashboard is
        /// not the tab on screen, where a timer would only be spending frames.
        /// </summary>
        public static bool ShouldAdvance(bool pointerOver, bool onScreen) => onScreen && !pointerOver;

        /// <summary>The card at an index, or the first one when the index has drifted.</summary>
        public static BillboardCard CardAt(int index) => Cards[JumpTo(index, Cards.Length)];
    }
}
