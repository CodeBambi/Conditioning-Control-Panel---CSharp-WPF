using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// Remove and Block ask first, inline in the friend's card (a modal from inside a popup loses
/// its owner), and the accounts this PC blocked sit behind the foot's Blocked button with
/// Unblock (<see cref="FriendsBlockList"/>). Plugs into the hooks in FriendsDrawer.Honesty.cs.
/// </summary>
public sealed partial class FriendsDrawer
{
    /// <summary>The friend whose card is asking "are you sure", and about what ("remove" or "block").</summary>
    private (string Id, string What)? _confirm;

    /// <summary>True while the Blocked list is showing under the friends.</summary>
    private bool _showBlocked;

    internal (string Id, string What)? PendingConfirm => _confirm;
    internal bool ShowingBlocked => _showBlocked;

    partial void AskFirst(Friend f, string what, ref bool asked)
    {
        AskConfirm(f, what);
        asked = true;
    }

    partial void AddCardAsk(Friend f, StackPanel card, ref bool asking)
    {
        if (_confirm is not { } c || c.Id != f.Id) return;
        card.Children.Add(ConfirmStrip(f, c.What));
        asking = true;
    }

    partial void ForgetAsk() => _confirm = null;

    partial void DropStaleAsk(FriendsSnapshot snap)
    {
        if (_confirm is { } c && !ContainsFriend(snap, c.Id)) _confirm = null;
    }

    partial void OnBlocked(string id, string name)
    {
        try { FriendsBlockList.Shared.Add(FriendsBlockList.Account(), id, name, DateTimeOffset.UtcNow); }
        catch (Exception ex) { App.Logger?.Debug("[Friends] remember block: {E}", ex.Message); }
    }

    /// <summary>A menu's Remove or Block: the card opens on that friend with the question in it.</summary>
    internal void AskConfirm(Friend f, string what)
    {
        FriendsSfx.Click();
        _confirm = (f.Id, what);
        _openId = f.Id;
        _picker = null;
        Render();
    }

    private FrameworkElement ConfirmStrip(Friend f, string what)
    {
        bool block = what == "block";
        var box = new Border
        {
            Background = FriendsLook.Frozen(Color.FromArgb(0x1F, 0xFF, 0x5F, 0x7A)),
            BorderBrush = FriendsLook.Frozen(Color.FromArgb(0x66, 0xFF, 0x5F, 0x7A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 7, 7, 7),
            Margin = new Thickness(0, 6, 0, 2),
            Tag = "friends-confirm:" + what,
        };
        var sp = new StackPanel();
        var q = FriendsLook.Label(Loc.GetF(block ? "friends_confirm_block" : "friends_confirm_remove", f.Name),
            12.5, FriendsLook.TextBrush, null, FontWeights.SemiBold);
        q.TextWrapping = TextWrapping.Wrap;
        q.TextTrimming = TextTrimming.None;
        sp.Children.Add(q);
        var sub = FriendsLook.Label(Loc.Get(block ? "friends_confirm_block_sub" : "friends_confirm_remove_sub"), 11, FriendsLook.MutedBrush);
        sub.TextWrapping = TextWrapping.Wrap;
        sub.TextTrimming = TextTrimming.None;
        sub.Margin = new Thickness(0, 2, 0, 6);
        sp.Children.Add(sub);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var keep = FriendsLook.Pill(Loc.Get("friends_confirm_keep"), FriendsLook.RaisedBrush, FriendsLook.TextBrush,
            FriendsLook.Line2Brush, 8, new Thickness(10, 3, 10, 3));
        keep.FontSize = 12;
        keep.Tag = "friends-confirm-keep";
        keep.Click += (_, _) => { FriendsSfx.Click(); _confirm = null; Render(); };
        var yes = FriendsLook.Pill(Loc.Get(block ? "friends_menu_block" : "friends_confirm_yes_remove"), FriendsLook.RedBrush,
            FriendsLook.Frozen(FriendsLook.Rgb(0x2A, 0x06, 0x10)), FriendsLook.RedBrush, 8, new Thickness(10, 3, 10, 3), FriendsLook.RedBrush);
        yes.FontSize = 12;
        yes.Margin = new Thickness(6, 0, 0, 0);
        yes.Tag = "friends-confirm-yes";
        yes.Click += async (_, _) => await ConfirmAsync(f.Id, f.Name, what);
        buttons.Children.Add(keep);
        buttons.Children.Add(yes);
        sp.Children.Add(buttons);
        box.Child = sp;
        return box;
    }

    /// <summary>The second press of Remove or Block. Internal for the suite.</summary>
    internal Task<ActResult> ConfirmAsync(string id, string name, string what)
    {
        _confirm = null;
        return RemoveOrBlockAsync(id, name, what);
    }

    // ---- requests ---------------------------------------------------------------------

    partial void AddRequestMenuExtras(ContextMenu menu, FriendRequest r)
    {
        var block = MenuItem("block");
        block.Click += async (_, _) => await BlockRequesterAsync(r);
        menu.Items.Add(block);
    }

    /// <summary>Block from a request row. One tap in a menu, no question: blocking a stranger is
    /// undone from the Blocked list at no cost.</summary>
    internal async Task<ActResult> BlockRequesterAsync(FriendRequest r)
    {
        if (_svc == null) return ActResult.TryLater;
        ActResult res;
        try { res = await _svc.BlockAsync(r.Id); }
        catch { res = ActResult.TryLater; }
        if (res == ActResult.Done)
        {
            FriendsSfx.Dismiss();
            OnBlocked(r.Id, r.Name);
            TellOutside(Loc.GetF("friends_blocked_done", r.Name), good: true, always: true);
        }
        else ShowActResult("in:" + r.Id, res);
        await SafeRefreshAsync();
        Render();
        return res;
    }

    // ---- the Blocked list -------------------------------------------------------------

    partial void AddFootExtras(StackPanel left)
    {
        var glyph = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = _showBlocked ? FriendsLook.LilacBrush : FriendsLook.DimBrush,
        };
        var blocked = FriendsLook.Pill(glyph, Brushes.Transparent, FriendsLook.MutedBrush, Brushes.Transparent, 9,
            new Thickness(8, 6, 8, 6), FriendsLook.HoverBrush);
        blocked.Tag = "friends-blocked-toggle";
        blocked.ToolTip = Loc.Get("friends_blocked_title");
        blocked.IsEnabled = _svc?.Available == true;
        blocked.Click += (_, _) => ToggleBlocked();
        left.Children.Add(blocked);
    }

    /// <summary>The foot's Blocked button.</summary>
    internal void ToggleBlocked()
    {
        FriendsSfx.Click();
        _showBlocked = !_showBlocked;
        Render();
    }

    partial void ListExtrasShowing(ref bool showing) => showing |= _showBlocked;

    partial void AddListExtras()
    {
        if (!_showBlocked) return;
        IReadOnlyList<BlockedEntry> blocked;
        try { blocked = FriendsBlockList.Shared.For(FriendsBlockList.Account()); }
        catch { blocked = Array.Empty<BlockedEntry>(); }
        AddSection("friends_section_blocked", blocked.Count);
        if (blocked.Count == 0)
        {
            var t = FriendsLook.Label(Loc.Get("friends_blocked_empty"), 11.5, FriendsLook.MutedBrush);
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            t.Margin = new Thickness(10, 4, 10, 8);
            t.Tag = "friends-blocked-empty";
            _list.Children.Add(t);
            return;
        }
        foreach (var b in blocked) AddRow("blocked:" + b.Id, BuildBlockedRow(b));
    }

    private FrameworkElement BuildBlockedRow(BlockedEntry b)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 1, 0, 1),
            Tag = "friends-blocked:" + b.Id,
        };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var avatar = FriendsLook.Avatar(b.Name, null, 30);
        avatar.Opacity = 0.5;
        g.Children.Add(avatar);
        var mid = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(FriendsLook.Label(b.Name, 13, FriendsLook.MutedBrush, FriendsLook.Display, FontWeights.Medium));
        if (_results.TryGetValue("blocked:" + b.Id, out var res))
            mid.Children.Add(FriendsLook.Label(res.Text, 11, res.Good ? FriendsLook.MintBrush : FriendsLook.GoldBrush, FriendsLook.Display));
        Grid.SetColumn(mid, 1);
        g.Children.Add(mid);
        var unblock = FriendsLook.Pill(Loc.Get("friends_unblock"), FriendsLook.RaisedBrush, FriendsLook.TextBrush,
            FriendsLook.Line2Brush, 8, new Thickness(9, 3, 9, 3));
        unblock.FontSize = 12;
        unblock.Tag = "friends-unblock";
        unblock.Click += async (_, _) => await UnblockAsync(b);
        Grid.SetColumn(unblock, 2);
        g.Children.Add(unblock);
        row.Child = g;
        return row;
    }

    /// <summary>Unblock, and forget the block only once the server agreed. Internal for the suite.</summary>
    internal async Task<ActResult> UnblockAsync(BlockedEntry b)
    {
        if (_svc == null) return ActResult.TryLater;
        FriendsSfx.Click();
        ActResult r;
        try { r = await _svc.UnblockAsync(b.Id); }
        catch { r = ActResult.TryLater; }
        if (r == ActResult.Done)
        {
            try { FriendsBlockList.Shared.Remove(b.Account, b.Id); } catch { }
            FriendsSfx.Accepted();
            TellOutside(Loc.GetF("friends_unblocked_done", b.Name), good: true, always: true);
            Render();
        }
        else ShowActResult("blocked:" + b.Id, r);
        return r;
    }
}
