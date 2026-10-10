// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Tabs/LobbyRowView.cs. Deviations: the art
// is a ModArt bitmap (mod override first, then this head's avares copy) handed to the template as
// an ImageBrush; the depth brushes come from DepthPaint (Core DepthPalette / DepthRules) instead of
// the WPF theme lookup PageDepthPaint.Theme.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Controls.Depth;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services.Lobby;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>
/// One Lobby row as the page draws it. Built from a <see cref="LobbyRow"/>; every string goes
/// through Loc, nothing here decides anything (gates live in <see cref="LobbyGates"/>, order in
/// <see cref="LobbyMerge"/>).
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
    public IImage? Art => ArtFor(Row.Game);
    /// <summary>The art as the thumbnail's background; null leaves the plate empty.</summary>
    public IBrush? ArtBrush => ArtFor(Row.Game) is { } img ? new ImageBrush(img) { Stretch = Stretch.UniformToFill } : null;
    /// <summary>Stable identity across polls, for the "new table" pop.</summary>
    public string Id => Row.Game + "|" + (Row.Key ?? Row.HostName + "|" + Row.OpponentName) + "|" + Row.State;
    public bool Friend => Row.Friend;
    public string ButtonText { get; }
    public bool HasButton { get; }
    /// <summary>The join would be refused by the game's gate: the button shows with a lock.</summary>
    public bool Locked { get; }
    public double ButtonOpacity => Locked ? 0.55 : 1.0;

    // ---- depth: a lobby table is a FLOATING card, its Join / Watch button a RAISED plank.

    /// <summary>The Lobby lives in Social: its card shadows are tinted with Social's hue.</summary>
    public static uint DepthHue => NavStripRules.Accent("social");
    /// <summary>The 1 px rim of a floating card (DepthFloatRim).</summary>
    public IBrush? CardRim => SharedCardRim;
    /// <summary>The soft band under the card: ShadowColor(Social, FloatAlpha), FloatPx long.</summary>
    public IBrush CardShadow => SharedCardShadow;
    public double CardShadowPx => DepthRules.FloatPx;

    private static readonly IBrush? SharedCardRim = DepthPaint.Brush("DepthFloatRim");
    private static readonly IBrush SharedCardShadow = DepthPaint.ShadowBand(DepthHue, DepthRules.FloatAlpha);

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

    private static readonly Dictionary<LobbyGame, Bitmap?> ArtCache = new();

    public static string ArtPath(LobbyGame g) => g switch
    {
        LobbyGame.Chess => "features/piecebypiece.png",
        LobbyGame.Goon => "features/goon_game_tile.png",
        // A text-free crop of the Remote Hands quest art: the feature card says REMOTE CONTROL,
        // which a 40 px thumbnail cuts to "MOTE NTROL".
        _ => "features/lobby_remote.png",
    };

    public static Bitmap? ArtFor(LobbyGame g)
    {
        lock (ArtCache)
        {
            if (ArtCache.TryGetValue(g, out var hit)) return hit;
            Bitmap? img = null;
            try { img = ModArt.TryLoad(ArtPath(g), 96); }
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
