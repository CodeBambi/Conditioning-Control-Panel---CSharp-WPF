using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.PieceByPiece;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>
    /// LIVE, the pure half: open tables and friends in a game, off the Lobby's last snapshot.
    /// One card at most. A friend's table first (Join seats you at it), then the open tables
    /// (one table: Join; several: See tables opens the Lobby), then a friend in a game with no
    /// seat open (Lobby). Signed out, the snapshot is empty and so is the card.
    /// </summary>
    public static class LiveCards
    {
        public const string CardJoinFriend = "live.friend";
        public const string CardTables = "live.tables";
        public const string CardPlaying = "live.playing";

        /// <summary>The Callback target prefix: "join:chess:p_xxx", "join:goon:ABCD".</summary>
        public const string JoinPrefix = "join:";

        public static BillboardCardSpec? Decide(LobbySnapshot? snap, Func<string, string> loc)
        {
            if (snap == null || !snap.SignedIn) return null;
            var joinable = snap.Joinable;
            string eyebrow = loc("billboard_card_live_eyebrow");

            if (joinable.Count > 0)
            {
                var top = joinable[0];
                var game = GameLabel(top.Game, loc);
                if (top.Friend)
                {
                    return Card(CardJoinFriend, 0, eyebrow,
                        CardText.F(loc, "billboard_card_live_friend_title", top.HostName),
                        CardText.F(loc, "billboard_card_live_friend_line", game),
                        JoinAction(top, loc), joinable.Count);
                }

                var games = joinable.Select(r => r.Game).Distinct().OrderBy(g => g).Select(g => GameLabel(g, loc)).ToList();
                bool one = joinable.Count == 1;
                var title = one
                    ? loc("billboard_card_live_one_title")
                    : CardText.F(loc, "billboard_card_live_many_title", joinable.Count);
                var line = CardText.F(loc, one ? "billboard_card_live_line_one" : "billboard_card_live_line_many",
                    CardText.JoinAnd(loc, games));
                var action = one
                    ? JoinAction(top, loc)
                    : new BillboardAction(BillboardActionKind.Tab, "availablesubjects", loc("billboard_card_live_look"));
                return Card(CardTables, 1, eyebrow, title, line, action, joinable.Count);
            }

            var playing = snap.Friends.FirstOrDefault(r => r.State == LobbyRowState.Playing);
            if (playing != null)
            {
                return Card(CardPlaying, 2, eyebrow,
                    CardText.F(loc, "billboard_card_live_playing_title", playing.HostName),
                    CardText.F(loc, "billboard_card_live_playing_line", GameLabel(playing.Game, loc)),
                    new BillboardAction(BillboardActionKind.Tab, "availablesubjects", loc("billboard_card_live_lobby")), 0);
            }
            return null;
        }

        /// <summary>Chess and Goon join straight from the card; Remote (a claim, with its own
        /// confirmation on the Lobby page) opens the Lobby.</summary>
        internal static BillboardAction JoinAction(LobbyRow row, Func<string, string> loc)
        {
            if (row.CanJoin && row.Game is LobbyGame.Chess or LobbyGame.Goon)
                return new BillboardAction(BillboardActionKind.Callback, Target(row.Game, row.Key!), loc("billboard_card_live_join"));
            return new BillboardAction(BillboardActionKind.Tab, "availablesubjects", loc("billboard_card_live_look"));
        }

        public static string Target(LobbyGame game, string key) =>
            JoinPrefix + (game == LobbyGame.Chess ? "chess" : "goon") + ":" + key;

        /// <summary>Reads a Callback target back. False for anything this provider did not issue.</summary>
        public static bool TryParseTarget(string? target, out LobbyGame game, out string key)
        {
            game = LobbyGame.Chess;
            key = string.Empty;
            if (string.IsNullOrEmpty(target) || !target.StartsWith(JoinPrefix, StringComparison.Ordinal)) return false;
            var rest = target.Substring(JoinPrefix.Length);
            int colon = rest.IndexOf(':');
            if (colon <= 0 || colon == rest.Length - 1) return false;
            var kind = rest.Substring(0, colon);
            key = rest.Substring(colon + 1);
            if (kind == "chess") { game = LobbyGame.Chess; return true; }
            if (kind == "goon") { game = LobbyGame.Goon; return true; }
            key = string.Empty;
            return false;
        }

        public static string GameLabel(LobbyGame game, Func<string, string> loc) => loc(game switch
        {
            LobbyGame.Chess => "lobby_game_chess",
            LobbyGame.Goon => "lobby_game_goon",
            _ => "lobby_game_remote",
        });

        private static BillboardCardSpec Card(string id, int priority, string eyebrow, string title, string line,
            BillboardAction action, int count) =>
            new(id, BillboardCardKind.Live, priority, eyebrow, title, line, CardHues.Live,
                CardArt.Tables, new Dictionary<string, int> { ["count"] = count }, action);
    }

    /// <summary>LIVE, the adapter: reads <see cref="App.Lobby"/>'s snapshot (kept fresh by the
    /// panel's own lease while it is on screen) and joins through each game's own door.</summary>
    public sealed class LiveProvider : BillboardProviderBase
    {
        private bool _hooked;

        public override string Id => "live";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            var lobby = App.Lobby;
            if (lobby == null) return Array.Empty<BillboardCardSpec>();
            if (!_hooked) { lobby.Changed += _ => RaiseChanged(); _hooked = true; }
            var card = LiveCards.Decide(lobby.Snapshot, Loc);
            return card == null ? Array.Empty<BillboardCardSpec>() : new[] { card };
        });

        public override void Invoke(string actionTarget)
        {
            if (!LiveCards.TryParseTarget(actionTarget, out var game, out var key)) return;
            try
            {
                if (BackRoomApi.AppIdentity() == null) { App.MainWindowRef?.OpenUnifiedLoginDialog(); return; }
                if (game == LobbyGame.Chess) PieceByPieceHostService.JoinOpenTable(key);
                else GoonHostService.Launch(duckMainWindow: true, joinCode: key);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Billboard] live join {Game} failed", game); }
        }
    }
}
