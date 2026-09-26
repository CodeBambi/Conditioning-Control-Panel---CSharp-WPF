using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// The three pickers that open inside a friend's card, and the menu under "more". Pickers are
/// drawn INLINE in the card rather than as popups of their own: the drawer is already a popup,
/// and a popup inside a popup is where WPF's click-outside logic starts losing arguments.
/// </summary>
public sealed partial class FriendsDrawer
{
    private FrameworkElement BuildPicker(Friend f, string picker) => picker switch
    {
        "poke" => BuildPokePicker(f),
        "invite" => BuildInvitePicker(f),
        _ => BuildWatchPicker(f),
    };

    // ---- poke -------------------------------------------------------------------------

    private FrameworkElement BuildPokePicker(Friend f)
    {
        var wrap = new WrapPanel { Tag = "friends-picker:poke" };
        foreach (var id in PokeSet.Shipped)
        {
            var chip = FriendsLook.Pill(Loc.Get("friends_poke_" + id), FriendsLook.ButtonBrush, FriendsLook.TextBrush,
                FriendsLook.Line2Brush, 999, new Thickness(11, 5, 11, 5), FriendsLook.PinkBrush);
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.Tag = "friends-poke:" + id;
            chip.MouseEnter += (_, _) => chip.Foreground = FriendsLook.Frozen(FriendsLook.Rgb(0x2A, 0x0A, 0x1C));
            chip.MouseLeave += (_, _) => chip.Foreground = FriendsLook.TextBrush;
            chip.Click += async (_, _) =>
            {
                Pop(chip, FriendsLook.Pink);
                await PokeAsync(f.Id, id);
            };
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    /// <summary>Pokes a friend and words the answer in the row. Internal for the suite.</summary>
    internal async Task<SendResult> PokeAsync(string friendId, string pokeId)
    {
        if (_svc == null || !PokeSet.IsValid(pokeId)) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.PokeAsync(friendId, pokeId); }
        catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    // ---- invite -----------------------------------------------------------------------

    private static readonly (string Id, string Art)[] InviteTiles =
    {
        (InviteDestination.Goon, "features/goon_game_tile.png"),
        (InviteDestination.BackRoom, "features/backroom.png"),
        (InviteDestination.Remote, "features/remote_control.png"),
        (InviteDestination.Ramp, "features/Phrase_Lock.png"),
    };

    private FrameworkElement BuildInvitePicker(Friend f)
    {
        var grid = new UniformGrid { Columns = 2, Tag = "friends-picker:invite" };
        foreach (var (id, art) in InviteTiles)
        {
            var (code, blockedKey) = InviteCodes.For(id);
            var tile = InviteTile(id, art);
            tile.Margin = new Thickness(grid.Children.Count % 2 == 0 ? 0 : 3, 0, grid.Children.Count % 2 == 0 ? 3 : 0, 6);
            if (blockedKey != null)
            {
                tile.IsEnabled = false;
                // A disabled button shows no tooltip unless it is told to.
                ToolTipService.SetShowOnDisabled(tile, true);
                tile.ToolTip = Loc.Get(blockedKey);
            }
            else if (id == InviteDestination.Goon && string.IsNullOrEmpty(code))
            {
                tile.ToolTip = Loc.Get("friends_invite_goon_tip");
            }
            tile.Click += async (_, _) =>
            {
                Pop(tile, FriendsLook.Lilac);
                if (id == InviteDestination.Goon) await InviteToGoonAsync(f.Id);
                else await InviteAsync(f.Id, id, code);
            };
            grid.Children.Add(tile);
        }
        return grid;
    }

    private static Button InviteTile(string id, string art)
    {
        var sp = new StackPanel();
        var pic = FriendsLook.Art(art, 256);
        if (pic != null)
        {
            var img = new Image { Source = pic, Stretch = Stretch.UniformToFill, Height = 64 };
            var clip = new Border { CornerRadius = new CornerRadius(9, 9, 0, 0), ClipToBounds = true, Child = img, Height = 64 };
            img.Clip = new RectangleGeometry(new Rect(0, 0, 124, 64), 9, 9);
            sp.Children.Add(clip);
        }
        var t = FriendsLook.Label(Loc.Get("friends_invite_" + id), 12.5, FriendsLook.TextBrush, FriendsLook.Display);
        t.Margin = new Thickness(8, 5, 8, 6);
        sp.Children.Add(t);
        var b = FriendsLook.Pill(sp, FriendsLook.ButtonBrush, FriendsLook.TextBrush, FriendsLook.Line2Brush, 10,
            new Thickness(0));
        b.Tag = "friends-invite:" + id;
        b.MouseEnter += (_, _) => { if (b.IsEnabled) b.BorderBrush = FriendsLook.LilacBrush; };
        b.MouseLeave += (_, _) => b.BorderBrush = FriendsLook.Line2Brush;
        return b;
    }

    private bool _openingGoonRoom;

    /// <summary>The Goon tile: send the live room code, or open a room first and send its code
    /// the moment the game reports it. One tap either way. Internal for the suite.</summary>
    internal async Task<SendResult?> InviteToGoonAsync(string friendId)
    {
        var code = InviteCodes.GoonCode();
        if (string.IsNullOrEmpty(code))
        {
            if (!InviteCodes.CanHostGoon())
            {
                ShowNote(friendId, "friends_invite_goon_prime", good: false, timed: true);
                return null;
            }
            if (_openingGoonRoom) return null;   // a second tap while the room opens costs nothing
            _openingGoonRoom = true;
            ShowNote(friendId, "friends_invite_goon_opening");
            (string? Code, bool Busy) opened;
            try { opened = await InviteCodes.OpenGoonRoom(TimeSpan.FromSeconds(45)); }
            catch { opened = (null, false); }
            finally { _openingGoonRoom = false; }
            if (string.IsNullOrEmpty(opened.Code))
            {
                ShowNote(friendId, opened.Busy ? "friends_invite_goon_busy" : "friends_invite_goon_failed",
                    good: false, timed: true);
                return null;
            }
            code = opened.Code;
        }
        return await InviteAsync(friendId, InviteDestination.Goon, code);
    }

    internal async Task<SendResult> InviteAsync(string friendId, string destination, string? code)
    {
        if (_svc == null || !InviteDestination.IsValid(destination)) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.InviteAsync(friendId, destination, code); }
        catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    // ---- watch ------------------------------------------------------------------------

    private static readonly string[] WatchTabs = { "catalogue", "flavour", "ht" };

    private FrameworkElement BuildWatchPicker(Friend f)
    {
        var sp = new StackPanel { Tag = "friends-picker:watch" };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var tab in WatchTabs)
        {
            bool on = _watchTab == tab;
            var b = FriendsLook.Pill(Loc.Get("friends_watch_tab_" + tab),
                on ? FriendsLook.LilacBrush : Brushes.Transparent,
                on ? FriendsLook.Frozen(FriendsLook.Ground) : FriendsLook.MutedBrush,
                on ? FriendsLook.LilacBrush : FriendsLook.Line2Brush, 999, new Thickness(10, 3, 10, 3),
                on ? FriendsLook.LilacBrush : null);
            b.FontSize = 12;
            b.Margin = new Thickness(0, 0, 4, 0);
            b.Tag = "friends-watch-tab:" + tab;
            b.Click += (_, _) => { _watchTab = tab; Render(); };
            tabs.Children.Add(b);
        }
        sp.Children.Add(tabs);

        switch (_watchTab)
        {
            case "catalogue":
                sp.Children.Add(CatalogueList(f));
                break;
            case "ht":
                sp.Children.Add(HtBox(f));
                break;
            default:
                var wrap = new WrapPanel();
                foreach (var fl in WatchRef.Flavours)
                {
                    var chip = FriendsLook.Pill(Loc.Get("friends_flavour_" + fl), FriendsLook.ButtonBrush, FriendsLook.TextBrush,
                        FriendsLook.Line2Brush, 999, new Thickness(11, 5, 11, 5));
                    chip.Margin = new Thickness(0, 0, 6, 6);
                    chip.Tag = "friends-flavour:" + fl;
                    var title = Loc.Get("friends_flavour_" + fl);
                    chip.Click += async (_, _) =>
                    {
                        Pop(chip, FriendsLook.Gold);
                        await SendWatchAsync(f.Id, new WatchRef(WatchKind.Flavour, fl, title));
                    };
                    wrap.Children.Add(chip);
                }
                sp.Children.Add(wrap);
                break;
        }
        return sp;
    }

    private FrameworkElement CatalogueList(Friend f)
    {
        var items = CatalogueWatches.List();
        if (items.Count == 0)
        {
            var t = FriendsLook.Label(Loc.Get("friends_watch_catalogue_empty"), 12, FriendsLook.MutedBrush);
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            t.Tag = "friends-watch-catalogue-empty";
            return t;
        }
        var sp = new StackPanel();
        foreach (var (id, title) in items)
        {
            var b = FriendsLook.Pill(title, FriendsLook.ButtonBrush, FriendsLook.TextBrush, FriendsLook.Line2Brush, 9,
                new Thickness(10, 5, 10, 5));
            b.Margin = new Thickness(0, 0, 0, 4);
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Click += async (_, _) =>
            {
                Pop(b, FriendsLook.Gold);
                await SendWatchAsync(f.Id, new WatchRef(WatchKind.Catalogue, id, title));
            };
            sp.Children.Add(b);
        }
        return sp;
    }

    private FrameworkElement HtBox(Friend f)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox
        {
            MaxLength = 8,
            FontFamily = FriendsLook.Mono,
            FontSize = 13,
            Foreground = FriendsLook.TextBrush,
            Background = FriendsLook.Frozen(FriendsLook.Ground),
            BorderBrush = FriendsLook.Line2Brush,
            CaretBrush = FriendsLook.LilacBrush,
            Padding = new Thickness(6, 3, 6, 3),
            ToolTip = Loc.Get("friends_watch_ht_hint"),
            Tag = "friends-ht-box",
        };
        var send = FriendsLook.Pill(Loc.Get("friends_watch_send"), FriendsLook.GoldBrush,
            FriendsLook.Frozen(FriendsLook.Rgb(0x2A, 0x1C, 0x04)), FriendsLook.GoldBrush, 8, new Thickness(12, 4, 12, 4),
            FriendsLook.GoldBrush);
        send.Margin = new Thickness(6, 0, 0, 0);
        send.IsEnabled = false;
        box.TextChanged += (_, _) =>
        {
            var n = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (n != box.Text) { box.Text = n; box.CaretIndex = n.Length; }
            send.IsEnabled = n.Length > 0;
        };
        send.Click += async (_, _) =>
        {
            var id = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (id.Length == 0) return;
            Pop(send, FriendsLook.Gold);
            await SendWatchAsync(f.Id, new WatchRef(WatchKind.Ht, id, null));
        };
        g.Children.Add(box);
        Grid.SetColumn(send, 1);
        g.Children.Add(send);
        return g;
    }

    internal async Task<SendResult> SendWatchAsync(string friendId, WatchRef watch)
    {
        if (_svc == null || !watch.IsValid()) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.SendWatchAsync(friendId, watch); }
        catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    // ---- the menu (right-click and "more") --------------------------------------------

    private ContextMenu BuildMenu(Friend f)
    {
        var menu = new ContextMenu
        {
            Background = FriendsLook.Frozen(FriendsLook.Rgb(0x22, 0x16, 0x41)),
            BorderBrush = FriendsLook.Line2Brush,
            Foreground = FriendsLook.TextBrush,
            FontFamily = FriendsLook.Display,
            FontSize = 13.5,
            Tag = "friends-menu:" + f.Id,
        };
        foreach (var item in FriendsDrawerRules.MenuItems(f.Squelched))
        {
            if (item == null) { menu.Items.Add(new Separator { Background = FriendsLook.LineBrush }); continue; }
            var mi = MenuItem(item);
            if (item == "report")
            {
                foreach (var reason in ReportReason.All)
                {
                    var sub = MenuItem("report_" + reason);
                    var rr = reason;
                    sub.Click += async (_, _) =>
                    {
                        try { if (_svc != null) await _svc.ReportAsync(f.Id, rr); } catch { }
                        ShowNote(f.Id, "friends_report_done");
                    };
                    mi.Items.Add(sub);
                }
            }
            else
            {
                var what = item;
                mi.Click += async (_, _) => await RunMenuAsync(f, what);
            }
            menu.Items.Add(mi);
        }
        return menu;
    }

    private static MenuItem MenuItem(string id)
    {
        bool warn = id is "block" or "report";
        return new MenuItem
        {
            Header = Loc.Get("friends_menu_" + id),
            Foreground = warn ? FriendsLook.RedBrush : FriendsLook.TextBrush,
            Background = Brushes.Transparent,
            Tag = "friends-menu-item:" + id,
        };
    }

    /// <summary>Runs one menu line. Internal for the suite.</summary>
    internal async Task RunMenuAsync(Friend f, string what)
    {
        if (_svc == null) return;
        try
        {
            switch (what)
            {
                case "squelch": await _svc.SetSquelchAsync(f.Id, true); break;
                case "unsquelch": await _svc.SetSquelchAsync(f.Id, false); break;
                case "remove": await _svc.RemoveAsync(f.Id); _openId = null; break;
                case "block": await _svc.BlockAsync(f.Id); _openId = null; break;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] menu {What} failed: {E}", what, ex.Message); }
        await SafeRefreshAsync();
        Render();
    }

    /// <summary>A worded note in the row that is not a send result (a report went through).</summary>
    private void ShowNote(string friendId, string key, bool good = true, bool timed = false)
    {
        if (timed) { ShowTimed(friendId, Loc.Get(key), good, TimeSpan.FromSeconds(4)); return; }
        if (_resultTimers.TryGetValue(friendId, out var old)) { old.Stop(); _resultTimers.Remove(friendId); }
        _results[friendId] = (Loc.Get(key), good);
        Render();
    }
}

/// <summary>
/// Where the Goon and Remote invite tiles get their live join code. Remote reads the running
/// session; the Goon Game page reports the room it is hosting (<c>room-code</c>) and
/// <see cref="GoonHostService.RoomCode"/> holds it. With no room yet the Goon tile opens one
/// (<see cref="OpenGoonRoom"/>) and sends its code, so it is only disabled for an account
/// that cannot host. Backroom and Ramp carry no code.
/// </summary>
public static class InviteCodes
{
    /// <summary>The live Goon Game join code while this account is hosting, else null.</summary>
    public static Func<string?> GoonCode { get; set; } = () => GoonHostService.RoomCode;

    /// <summary>May this account host a Goon room (Prime)? Joining is free, hosting is not.</summary>
    public static Func<bool> CanHostGoon { get; set; } = () => GoonHostService.CanHost;

    /// <summary>Opens (or reuses) a Goon room and returns its code; Busy = a match is on.</summary>
    public static Func<TimeSpan, Task<(string? Code, bool Busy)>> OpenGoonRoom { get; set; }
        = GoonHostService.OpenRoomForInviteAsync;

    /// <summary>The live Remote Control session code, else null.</summary>
    public static Func<string?> RemoteCode { get; set; } = () =>
    {
        try
        {
            var rc = App.RemoteControl;
            return rc != null && rc.IsActive && !string.IsNullOrEmpty(rc.SessionCode) ? rc.SessionCode : null;
        }
        catch { return null; }
    };

    /// <summary>The code to send with an invite, and the loc key of the reason the tile is
    /// disabled (null when it can be sent).</summary>
    internal static (string? Code, string? BlockedKey) For(string destination)
    {
        switch (destination)
        {
            case InviteDestination.Goon:
                var g = GoonCode();
                if (!string.IsNullOrEmpty(g)) return (g, null);
                // No room yet: the tile opens one, unless this account cannot host at all.
                return CanHostGoon() ? (null, null) : (null, "friends_invite_goon_prime");
            case InviteDestination.Remote:
                var r = RemoteCode();
                return string.IsNullOrEmpty(r) ? (null, "friends_invite_needs_remote") : (r, null);
            default:
                return (null, null);
        }
    }
}

/// <summary>
/// The Catalogue tab's list: library entries that came from the site catalogue, as
/// (catalogue id, title). The local library does not record a catalogue id yet, so this is
/// empty and the tab says so; whoever teaches the library that id fills <see cref="List"/>.
/// </summary>
public static class CatalogueWatches
{
    public static Func<IReadOnlyList<(string Id, string Title)>> List { get; set; }
        = () => Array.Empty<(string, string)>();
}
