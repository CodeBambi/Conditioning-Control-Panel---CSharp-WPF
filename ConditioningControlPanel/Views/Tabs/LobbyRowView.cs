using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;

namespace ConditioningControlPanel.Views.Tabs;

/// <summary>
/// One Lobby row as the page and the launcher dropdown draw it. Built from a
/// <see cref="LobbyRow"/>; every string goes through Loc, nothing here decides anything
/// (gates live in <see cref="LobbyGates"/>, order in <see cref="LobbyMerge"/>).
/// </summary>
public sealed class LobbyRowView
{
    public LobbyRow Row { get; }
    public string GameIcon { get; }
    public string GameLabel { get; }
    public string Title { get; }
    public string Summary { get; }
    public bool HasSummary => Summary.Length > 0;
    public bool Friend => Row.Friend;
    public string ButtonText { get; }
    public bool HasButton { get; }
    /// <summary>The join would be refused by the game's gate: the button shows with a lock.</summary>
    public bool Locked { get; }
    public double ButtonOpacity => Locked ? 0.55 : 1.0;

    public LobbyRowView(LobbyRow row, LobbyGates gates, Func<string, string>? loc = null)
    {
        loc ??= Loc.Get;
        Row = row;
        GameIcon = IconFor(row.Game);
        GameLabel = loc(LabelKey(row.Game));
        Title = row.State == LobbyRowState.Playing && !string.IsNullOrEmpty(row.OpponentName)
            ? string.Format(loc("lobby_row_versus"), row.HostName, row.OpponentName)
            : row.HostName;
        Summary = LobbySummary.For(row, loc);
        HasButton = row.CanJoin || row.CanWatch;
        Locked = row.CanJoin && !gates.CanJoin(row.Game);
        ButtonText = row.CanJoin ? (Locked ? "\U0001F512 " : "") + loc("lobby_btn_join") : loc("lobby_btn_watch");
    }

    public static string IconFor(LobbyGame g) => g switch
    {
        LobbyGame.Chess => "♟️",
        LobbyGame.Goon => "\U0001F497",
        _ => "\U0001F6F0️",
    };

    public static string LabelKey(LobbyGame g) => g switch
    {
        LobbyGame.Chess => "lobby_game_chess",
        LobbyGame.Goon => "lobby_game_goon",
        _ => "lobby_game_remote",
    };

    public static List<LobbyRowView> From(IEnumerable<LobbyRow> rows, LobbyGates gates, Func<string, string>? loc = null) =>
        rows.Select(r => new LobbyRowView(r, gates, loc)).ToList();
}
