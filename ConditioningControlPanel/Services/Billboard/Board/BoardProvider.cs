using System;
using System.Collections.Generic;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>What the board card's art view receives: the picture, and who to tell once it is on screen.</summary>
    public sealed record BoardArtData(BoardPicture Picture, Action<int>? OnShown);

    /// <summary>
    /// The one Board card: the owner's latest post, when it is ready, still live, and meant for
    /// this player's plan. NEW on the first show of each version; never snoozable.
    /// </summary>
    public sealed class BoardProvider : IBillboardProvider
    {
        public const string CardAccent = "#ff4fa8";

        private readonly BoardService _service;
        private readonly Func<string, string> _loc;

        public BoardProvider() : this(BoardService.Shared, Loc.Get) { }

        public BoardProvider(BoardService service, Func<string, string> loc)
        {
            _service = service;
            _loc = loc;
            _service.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }

        public string Id => "board";

        public event EventHandler? Changed;

        public IEnumerable<BillboardCardSpec> Current(BillboardContext context)
        {
            var card = Decide(_service.Current, context, _service.SeenVersion, _loc, _service.MarkShown);
            return card == null ? Array.Empty<BillboardCardSpec>() : new[] { card };
        }

        /// <summary>The board card never issues a Callback: its button is a tab or a link.</summary>
        public void Invoke(string actionTarget) { }

        /// <summary>The whole decision, pure.</summary>
        public static BillboardCardSpec? Decide(BoardPicture? picture, BillboardContext context, int seenVersion,
            Func<string, string> loc, Action<int>? onShown = null)
        {
            if (picture == null) return null;
            var post = picture.Post;
            if (!BoardRules.IsLive(post, context.NowUtc)) return null;
            if (!BoardRules.Sees(post.Audience, context.Tier)) return null;

            return new BillboardCardSpec(
                Id: "board:" + post.Version,
                Kind: BillboardCardKind.Board,
                Priority: 0,
                Eyebrow: loc("board_card_eyebrow"),
                Title: loc("board_card_title"),
                Line: string.Empty,
                AccentHex: CardAccent,
                ArtKey: BoardArtRegistration.Key,
                ArtData: new BoardArtData(picture, onShown),
                Action: BoardLinks.ToAction(post.Link, loc),
                Badge: post.Version > seenVersion ? BillboardBadge.New : BillboardBadge.None,
                Snoozable: false);
        }
    }
}
