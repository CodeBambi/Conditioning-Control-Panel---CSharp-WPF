using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    /// <summary>The second line on a card: game, then the settings.</summary>
    public string Line => HasSummary ? GameLabel + " · " + Summary : GameLabel;
    /// <summary>The game's feature art, small: light and coloured, readable on a dark card.</summary>
    public ImageSource? Art => ArtFor(Row.Game);
    /// <summary>Stable identity across polls, for the "new table" pop.</summary>
    public string Id => Row.Game + "|" + (Row.Key ?? Row.HostName + "|" + Row.OpponentName) + "|" + Row.State;
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

    private static readonly Dictionary<LobbyGame, ImageSource?> ArtCache = new();

    public static string ArtPath(LobbyGame g) => g switch
    {
        LobbyGame.Chess => "features/piecebypiece.png",
        LobbyGame.Goon => "features/goon_game_tile.png",
        _ => "features/remote_control.png",
    };

    public static ImageSource? ArtFor(LobbyGame g)
    {
        lock (ArtCache)
        {
            if (ArtCache.TryGetValue(g, out var hit)) return hit;
            ImageSource? img = null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri("pack://application:,,,/Resources/" + ArtPath(g), UriKind.Absolute);
                bmp.DecodePixelWidth = 96;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                img = bmp;
            }
            catch { img = null; }
            ArtCache[g] = img;
            return img;
        }
    }

    public static string LabelKey(LobbyGame g) => g switch
    {
        LobbyGame.Chess => "lobby_game_chess",
        LobbyGame.Goon => "lobby_game_goon",
        _ => "lobby_game_remote",
    };

    public static List<LobbyRowView> From(IEnumerable<LobbyRow> rows, LobbyGates gates, Func<string, string>? loc = null) =>
        rows.Select(r => new LobbyRowView(r, gates, loc)).ToList();
}
