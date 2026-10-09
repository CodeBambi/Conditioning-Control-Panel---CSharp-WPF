// PORTED from ConditioningControlPanel/Controls/Friends/FriendsDrawer.Pickers.cs (+ FriendsDrawer.cs
// BuildCard/ActionButton/ShowResult): the card's Invite / Poke / Send a watch buttons and the picker
// each opens inline in the card, over IFriendsService.
// ponytail: no Segoe MDL2 glyphs on the action buttons (no such font on Linux), no Pop() juice on a sent chip. The Goon
// and chess tiles stay shut here: GoonHostService and PieceByPieceHostService are WPF-only, so this head
// can neither open a room nor a board (FriendsInviteCodes is the seam that lights them when they move).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    private static readonly IBrush ButtonBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x33)),
        ButtonHover = new SolidColorBrush(Color.FromRgb(0x3A, 0x1F, 0x66)), Ground = new SolidColorBrush(Color.FromRgb(0x10, 0x0A, 0x1E)),
        PokeInk = new SolidColorBrush(Color.FromRgb(0x2A, 0x0A, 0x1C));

    /// <summary>The picker open inside the open card: "poke", "invite", "watch" or null.</summary>
    private string? _picker;
    private string _watchTab = "flavour";
    internal string? OpenPicker => _picker;

    /// <summary>Opens a picker in a friend's card (the suite's way to press an action).</summary>
    internal void OpenPickerFor(string friendId, string picker) { (_openId, _confirm, _picker) = (friendId, null, picker); Render(); }

    /// <summary>WPF BuildCard: Invite, Poke, Send a watch in two columns (FriendsDrawerRules.CardActions).</summary>
    private Control CardActions(Friend f)
    {
        var grid = new UniformGrid { Columns = 2 };
        foreach (var act in FriendsDrawerRules.CardActions)
        {
            if (act == "more") continue;
            bool lit = _picker == act;
            var b = Pill(Loc.Get("friends_action_" + act), lit ? ButtonHover : ButtonBg, Text, "friends-action:" + act, lit ? Lilac : Line2);
            (b.CornerRadius, b.Padding, b.HorizontalAlignment) = (new CornerRadius(10), new Thickness(10, 8, 10, 8), HorizontalAlignment.Stretch);
            b.Margin = grid.Children.Count % 2 == 0 ? new Thickness(0, 0, 3, 6) : new Thickness(3, 0, 0, 6);
            b.Click += (_, _) => { Friends.FriendsSfx.Click(); _picker = _picker == act ? null : act; Render(); };
            grid.Children.Add(b);
        }
        return grid;
    }

    private Control Picker(Friend f) => _picker switch
    {
        "poke" => PokePicker(f),
        "invite" => InvitePicker(f),
        _ => WatchPicker(f),
    };

    private Control PokePicker(Friend f)
    {
        var wrap = new WrapPanel { Tag = "friends-picker:poke" };
        foreach (var id in PokeSet.Shipped)
        {
            var chip = Chip(Loc.Get("friends_poke_" + id), "friends-poke:" + id);
            // WPF hover: pink pill, dark ink (Fluent reads these per button on :pointerover / :pressed).
            foreach (var state in new[] { "PointerOver", "Pressed" })
                (chip.Resources["ButtonBackground" + state], chip.Resources["ButtonForeground" + state], chip.Resources["ButtonBorderBrush" + state]) = (Pink, PokeInk, Pink);
            chip.Click += async (_, _) => await PokeAsync(f.Id, id);
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    internal async Task<SendResult> PokeAsync(string friendId, string pokeId)
    {
        if (_svc == null || !PokeSet.IsValid(pokeId)) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.PokeAsync(friendId, pokeId); } catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    /// <summary>One tile per sendable destination, in WPF's order and art (Remote is gone, owner 2026-09-28).</summary>
    internal static readonly (string Id, string Art)[] InviteTiles =
    {
        (InviteDestination.Goon, "goon_game_tile.png"), (InviteDestination.BackRoom, "backroom.png"),
        (InviteDestination.Ramp, "Phrase_Lock.png"), (InviteDestination.Chess, "piecebypiece.png"),
    };

    private Control InvitePicker(Friend f)
    {
        var grid = new UniformGrid { Columns = 2, Tag = "friends-picker:invite" };
        foreach (var (id, art) in InviteTiles)
        {
            var tile = InviteTile(id, art);
            tile.Margin = grid.Children.Count % 2 == 0 ? new Thickness(0, 0, 3, 6) : new Thickness(3, 0, 0, 6);
            if (FriendsInviteCodes.BlockedKey(id) is { } blocked)
            {
                tile.IsEnabled = false;
                ToolTip.SetShowOnDisabled(tile, true);
                ToolTip.SetTip(tile, Loc.Get(blocked));
            }
            tile.Click += async (_, _) =>
            {
                if (id == InviteDestination.Goon) await InviteAsync(f.Id, id, FriendsInviteCodes.GoonCode());
                else if (id == InviteDestination.Chess) await InviteToChessAsync(f.Id);
                else await InviteAsync(f.Id, id, null);
            };
            grid.Children.Add(tile);
        }
        return grid;
    }

    private static Button InviteTile(string id, string art)
    {
        var sp = new StackPanel();
        if (Art(art) is { } pic)
            sp.Children.Add(new Border { Height = 64, CornerRadius = new CornerRadius(9, 9, 0, 0), ClipToBounds = true,
                Child = new Image { Source = pic, Stretch = Stretch.UniformToFill, Height = 64 } });
        var t = Label(Loc.Get("friends_invite_" + id), 12.5, Text, Display);
        t.Margin = new Thickness(8, 5, 8, 6);
        sp.Children.Add(t);
        var b = Pill(sp, ButtonBg, Text, "friends-invite:" + id, Line2);
        (b.CornerRadius, b.Padding, b.HorizontalAlignment, b.HorizontalContentAlignment) =
            (new CornerRadius(10), new Thickness(0), HorizontalAlignment.Stretch, HorizontalAlignment.Stretch);
        b.PointerEntered += (_, _) => { if (b.IsEnabled) b.BorderBrush = Lilac; };
        b.PointerExited += (_, _) => b.BorderBrush = Line2;
        return b;
    }

    private static readonly Dictionary<string, Bitmap?> ArtCache = new();
    private static Bitmap? Art(string file)
    {
        if (ArtCache.TryGetValue(file, out var hit)) return hit;
        Bitmap? bmp = null;
        try
        {
            var uri = new Uri("avares://CCP.Avalonia/Resources/features/" + file);
            if (AssetLoader.Exists(uri)) { using var s = AssetLoader.Open(uri); bmp = Bitmap.DecodeToWidth(s, 256); }
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] invite art {File}: {E}", file, ex.Message); }
        return ArtCache[file] = bmp;
    }

    private bool _openingChess;

    /// <summary>WPF InviteToChessAsync: open the board on a challenge, then send its id. One tap.</summary>
    internal async Task<SendResult?> InviteToChessAsync(string friendId)
    {
        if (_openingChess) return null;
        _openingChess = true;
        ShowNote(friendId, "friends_invite_chess_opening");
        string? challenge;
        try { challenge = await FriendsInviteCodes.ChallengeFriend(friendId, TimeSpan.FromSeconds(45)); }
        catch { challenge = null; }
        finally { _openingChess = false; }
        if (!InviteDestination.IsChallengeId(challenge))
        {
            ShowTimed(friendId, Loc.Get("friends_invite_chess_failed"), false);
            return null;
        }
        return await InviteAsync(friendId, InviteDestination.Chess, challenge);
    }

    internal async Task<SendResult> InviteAsync(string friendId, string destination, string? code)
    {
        if (_svc == null || !InviteDestination.IsSendable(destination)) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.InviteAsync(friendId, destination, code); } catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    private static readonly string[] WatchTabs = { "catalogue", "flavour", "ht" };

    private Control WatchPicker(Friend f)
    {
        var sp = new StackPanel { Tag = "friends-picker:watch" };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var tab in WatchTabs)
        {
            bool on = _watchTab == tab;
            var b = Pill(Loc.Get("friends_watch_tab_" + tab), on ? Lilac : Brushes.Transparent, on ? Ground : Muted,
                "friends-watch-tab:" + tab, on ? Lilac : Line2);
            (b.CornerRadius, b.Padding, b.Margin) = (new CornerRadius(999), new Thickness(10, 3, 10, 3), new Thickness(0, 0, 4, 0));
            b.Click += (_, _) => { _watchTab = tab; Render(); };
            tabs.Children.Add(b);
        }
        sp.Children.Add(tabs);
        if (_watchTab == "catalogue") sp.Children.Add(CatalogueList(f));
        else if (_watchTab == "ht") sp.Children.Add(HtBox(f));
        else
        {
            var wrap = new WrapPanel();
            foreach (var fl in WatchRef.Flavours)
            {
                var title = Loc.Get("friends_flavour_" + fl);
                var chip = Chip(title, "friends-flavour:" + fl);
                chip.Click += async (_, _) => await SendWatchAsync(f.Id, new WatchRef(WatchKind.Flavour, fl, title));
                wrap.Children.Add(chip);
            }
            sp.Children.Add(wrap);
        }
        return sp;
    }

    private Control CatalogueList(Friend f)
    {
        var items = FriendsInviteCodes.CatalogueWatches();
        if (items.Count == 0) return Tagged(Wrap(Label(Loc.Get("friends_watch_catalogue_empty"), 12, Muted)), "friends-watch-catalogue-empty");
        var sp = new StackPanel();
        foreach (var (id, title) in items)
        {
            var b = Pill(title, ButtonBg, Text, "friends-catalogue:" + id, Line2);
            (b.Margin, b.HorizontalAlignment, b.HorizontalContentAlignment) = (new Thickness(0, 0, 0, 4), HorizontalAlignment.Stretch, HorizontalAlignment.Left);
            b.Click += async (_, _) => await SendWatchAsync(f.Id, new WatchRef(WatchKind.Catalogue, id, title));
            sp.Children.Add(b);
        }
        return sp;
    }

    private Control HtBox(Friend f)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var box = new TextBox
        {
            MaxLength = 8, FontFamily = Mono, FontSize = 13, Foreground = Text, Background = Ground, BorderBrush = Line2,
            CaretBrush = Lilac, Padding = new Thickness(6, 3, 6, 3), Tag = "friends-ht-box",
        };
        ToolTip.SetTip(box, Loc.Get("friends_watch_ht_hint"));
        var send = Pill(Loc.Get("friends_watch_send"), Gold, new SolidColorBrush(Color.FromRgb(0x2A, 0x1C, 0x04)), "friends-ht-send", Gold);
        (send.Margin, send.IsEnabled) = (new Thickness(6, 0, 0, 0), false);
        // Text property, not TextChanged: Avalonia raises that one later, so the Send state lagged a frame.
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            var n = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (n != box.Text) { box.Text = n; box.CaretIndex = n.Length; }
            send.IsEnabled = n.Length > 0;
        };
        send.Click += async (_, _) =>
        {
            var id = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (id.Length > 0) await SendWatchAsync(f.Id, new WatchRef(WatchKind.Ht, id, null));
        };
        Grid.SetColumn(send, 1);
        g.Children.Add(box);
        g.Children.Add(send);
        return g;
    }

    internal async Task<SendResult> SendWatchAsync(string friendId, WatchRef watch)
    {
        if (_svc == null || !watch.IsValid()) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.SendWatchAsync(friendId, watch); } catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    private static Button Chip(string text, string tag)
    {
        var chip = Pill(text, ButtonBg, Text, tag, Line2);
        (chip.CornerRadius, chip.Padding, chip.Margin) = (new CornerRadius(999), new Thickness(11, 5, 11, 5), new Thickness(0, 0, 6, 6));
        return chip;
    }

    /// <summary>WPF ShowResult: worded in the row; a drawer that folded while the send was out says it outside.</summary>
    private void ShowResult(string friendId, SendResult r)
    {
        var text = Loc.Get(FriendsDrawerRules.SendResultKey(r));
        if (FriendsDrawerRules.IsGood(r)) Friends.FriendsSfx.Sent(); else Friends.FriendsSfx.Denied();
        ShowTimed(friendId, text, FriendsDrawerRules.IsGood(r));
        if (!_isOpen) Say(text, FriendsDrawerRules.IsGood(r));
    }

    /// <summary>WPF ShowNote (untimed): a line that stays until the next result replaces it.</summary>
    private void ShowNote(string friendId, string key) { _results[friendId] = (Loc.Get(key), true); Render(); }
}

/// <summary>WPF InviteCodes + CatalogueWatches. This head hosts neither a Goon room nor a chess board, so
/// both tiles are shut ("not on this build"); whoever ports a host sets these.</summary>
internal static class FriendsInviteCodes
{
    public static Func<string?> GoonCode { get; set; } = () => null;
    public static Func<string, TimeSpan, Task<string?>> ChallengeFriend { get; set; } = (_, _) => Task.FromResult<string?>(null);
    public static Func<bool> HostsChess { get; set; } = () => false;
    public static Func<IReadOnlyList<(string Id, string Title)>> CatalogueWatches { get; set; } = () => Array.Empty<(string, string)>();

    /// <summary>The loc key of why a tile is shut, or null when it can be sent.</summary>
    internal static string? BlockedKey(string destination) => destination switch
    {
        InviteDestination.Goon when string.IsNullOrEmpty(GoonCode()) => "exclusives_not_on_this_build",
        InviteDestination.Chess when !HostsChess() => "exclusives_not_on_this_build",
        _ => null,
    };
}
