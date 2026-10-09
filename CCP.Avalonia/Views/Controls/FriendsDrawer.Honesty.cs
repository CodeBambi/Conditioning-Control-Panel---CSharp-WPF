// PORTED from ConditioningControlPanel/Controls/Friends/FriendsDrawer.Honesty.cs + the request menu and
// Blocked-list half of FriendsDrawer.Blocked.cs (7.1.5). Nothing cheers before the server said yes;
// a result for a drawer that has folded (a game took the screen) is said where the player is looking
// instead. A request row carries a menu (Block, Report by reason). The Blocked list is the server's
// list when it sends one, else what this PC remembers (FriendsBlockList, an old server).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Friends;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    /// <summary>Open as far as the result words go. The suite sets it without the open's network calls.</summary>
    internal bool IsOpen { get => _isOpen; set => _isOpen = value; }

    /// <summary>Says <paramref name="text"/> outside the drawer when the drawer folded while the call
    /// was out, or <paramref name="always"/> when the row that would carry it is gone.</summary>
    private void TellOutside(string text, bool good, bool always = false)
    {
        if (_isOpen && !always) return;
        try { Say(text, good); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] tell: {E}", ex.Message); }
    }

    /// <summary>A block went through: remember it on this PC only for a server that lists none.</summary>
    private void OnBlocked(string id, string name)
    {
        if (_last.Blocked != null) return;
        try { FriendsBlockList.Shared.Add(FriendsBlockList.Account(), id, name, DateTimeOffset.UtcNow); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] remember block: {E}", ex.Message); }
    }

    /// <summary>Who shows under Blocked: the server's list when it sends one (the truth, every
    /// device), else what this PC remembers (a server that predates the list).</summary>
    internal IReadOnlyList<BlockedEntry> BlockedRows()
    {
        var server = _last.Blocked;
        if (server != null)
        {
            var account = FriendsBlockList.Account() ?? "";
            var rows = new List<BlockedEntry>(server.Count);
            foreach (var b in server) rows.Add(new BlockedEntry(account, b.Id, b.Name, DateTimeOffset.MinValue));
            return rows;
        }
        try { return FriendsBlockList.Shared.For(FriendsBlockList.Account()); }
        catch { return Array.Empty<BlockedEntry>(); }
    }

    /// <summary>WPF BuildRequestMenu: Block (one tap, no question), then Report by reason.</summary>
    private ContextMenu RequestMenu(FriendRequest r)
    {
        var block = MenuItem("block");
        block.Click += async (_, _) => await BlockRequesterAsync(r);
        var report = MenuItem("report");
        var subs = new List<MenuItem>();
        foreach (var reason in ReportReason.All)
        {
            var sub = MenuItem("report_" + reason);
            var rr = reason;
            sub.Click += async (_, _) => await ReportAsync(r.Id, rr, "in:" + r.Id);
            subs.Add(sub);
        }
        report.ItemsSource = subs;
        return new ContextMenu
        {
            ItemsSource = new List<Control> { block, report }, Tag = "friends-request-menu:" + r.Id,
            Placement = global::Avalonia.Controls.PlacementMode.Pointer,
            Background = MenuGlass, BorderBrush = Line2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Foreground = Text, FontFamily = Display, FontSize = 13.5, Padding = new Thickness(4),
        };
    }

    /// <summary>Block from a request row. One tap in a menu, no question: blocking a stranger is
    /// undone from the Blocked list at no cost.</summary>
    internal async Task<ActResult> BlockRequesterAsync(FriendRequest r)
    {
        if (_svc == null) return ActResult.TryLater;
        ActResult res;
        try { res = await _svc.BlockAsync(r.Id); } catch { res = ActResult.TryLater; }
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

    private void ShowActResult(string rowId, ActResult r)
    {
        FriendsSfx.Denied();
        ShowTimed(rowId, Loc.Get(FriendsDrawerRules.ActResultKey(r)), false);
    }
}
