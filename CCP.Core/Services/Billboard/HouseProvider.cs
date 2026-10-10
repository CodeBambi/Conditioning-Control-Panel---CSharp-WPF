using System;
using System.Collections.Generic;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Billboard
{
    /// <summary>
    /// The house cards (Tonight Board, 2026-10-07): what is left of the old billboard roster, now
    /// filler at the back of the deck. Discord always; Web App, Remix and Loom take turns beside it
    /// (<see cref="DashboardBillboard.PickHouse"/>); Support never shows to a Prime player, who is
    /// already doing it. Every card keeps its own 16:9 poster from <c>Resources/billboard/</c>.
    /// </summary>
    public sealed class HouseProvider : IBillboardProvider
    {
        /// <summary>The support page the rest of the app already links to.</summary>
        public const string PatreonUrl = "https://www.patreon.com/CodeBambi";

        /// <summary>The Callback target of the Daily Daze card: open the Back Room in the app.</summary>
        public const string BackRoomCallback = "backroom";

        /// <summary>
        /// One house card as data: id, loc stem, poster, hue, target, button key. A card with
        /// <paramref name="Callback"/> set runs <see cref="Invoke"/> with its target instead of
        /// opening a link. The poster path's stem picks the drawn scene
        /// (<c>Controls/Billboard/HouseScenes</c>); a drawn-only card has no file behind it.
        /// </summary>
        public sealed record HouseCard(string Id, string Stem, string Poster, string Hue, string Url, string ButtonKey, bool HiddenForPrime = false, bool Callback = false);

        /// <summary>
        /// The house, pinned card first. External cards on cclabs.app carry <c>from=panel</c> so the
        /// visit is counted where it lands. The community card opens the server invite, never
        /// <c>ShowTab("discord")</c> (that is the in-app Discord PROFILE page).
        /// </summary>
        public static readonly IReadOnlyList<HouseCard> Cards = new[]
        {
            new HouseCard("house.discord", "discord", "billboard/discord.png", "#7b86ff", DiscordLinks.Invite, "billboard_deck_btn_join"),
            new HouseCard("house.webapp", "webapp", "billboard/webapp.png", "#5fe3ff", "https://app.cclabs.app/?from=panel", "billboard_deck_btn_open"),
            new HouseCard("house.remix", "remix", "billboard/remix.png", "#ff4fa8", "https://cclabs.app/remix/?from=panel", "billboard_deck_btn_open"),
            new HouseCard("house.loom", "loom", "billboard/loom.png", "#9b7bff", "https://cclabs.app/loom/?from=panel", "billboard_deck_btn_open"),
            new HouseCard("house.support", "support", "billboard/support.png", "#ffc94a", PatreonUrl, "billboard_deck_btn_support", HiddenForPrime: true),
            // The Daily Daze: one free wheel spin a day in the Back Room, free for everyone.
            new HouseCard("house.backroom", "backroom", "billboard/backroom.png", "#ffc94a", BackRoomCallback, "billboard_deck_btn_take_seat", Callback: true),
        };

        /// <summary>What opening the Back Room runs (sign-in first, then the room). Each head sets
        /// it at startup (WPF BillboardWiring.Start, Avalonia MainShellWindow.DashboardBillboard);
        /// tests replace it.</summary>
        internal static Action OpenBackRoom { get; set; } = () => { };

        private readonly Func<string, string> _loc;

        public HouseProvider() : this(null) { }

        /// <summary>Test seam: the localiser.</summary>
        internal HouseProvider(Func<string, string>? localize) => _loc = localize ?? (k => Loc.Get(k));

        public string Id => "house";

        public event EventHandler? Changed { add { } remove { } }

        public IEnumerable<BillboardCardSpec> Current(BillboardContext context)
        {
            for (int i = 0; i < Cards.Count; i++)
            {
                var c = Cards[i];
                if (c.HiddenForPrime && context.Tier == BillboardTier.Prime) continue;
                yield return new BillboardCardSpec(
                    Id: c.Id,
                    Kind: BillboardCardKind.House,
                    Priority: i,
                    Eyebrow: _loc($"billboard_{c.Stem}_eyebrow"),
                    Title: _loc($"billboard_{c.Stem}_title"),
                    Line: _loc($"billboard_{c.Stem}_line"),
                    AccentHex: c.Hue,
                    ArtKey: BuiltInArtKeys.Poster,
                    ArtData: c.Poster,
                    Action: new BillboardAction(c.Callback ? BillboardActionKind.Callback : BillboardActionKind.Link, c.Url, _loc(c.ButtonKey)));
            }
        }

        /// <summary>The one house callback: the Daily Daze card takes a seat in the Back Room.</summary>
        public void Invoke(string actionTarget)
        {
            if (actionTarget != BackRoomCallback) return;
            try { OpenBackRoom(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[Billboard] Back Room from the house card failed"); }
        }
    }
}
