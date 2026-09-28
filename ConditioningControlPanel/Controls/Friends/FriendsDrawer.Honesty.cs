using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// The honesty pass (2026-09-28): nothing cheers before the server said yes. A request row
/// answers Accept or Decline (Report one menu away), every list change words its result in the
/// row, and a result for a drawer that has folded (a game took the screen) is said where the
/// player is looking instead. The confirm step and the Blocked list plug in through the partial
/// hooks at the foot of this file (FriendsDrawer.Blocked.cs).
/// </summary>
public sealed partial class FriendsDrawer
{
    /// <summary>True between the chip opening the drawer and folding it.</summary>
    private bool _isOpen;

    /// <summary>Open as far as the result words go (the chip's OnOpened / OnClosed set it). The
    /// suite sets it without the open's network calls.</summary>
    internal bool IsOpen { get => _isOpen; set => _isOpen = value; }

    /// <summary>Remove or Block, for real. Internal for the suite.</summary>
    internal async Task<ActResult> RemoveOrBlockAsync(string id, string name, string what)
    {
        if (_svc == null) return ActResult.TryLater;
        ActResult r;
        try { r = what == "block" ? await _svc.BlockAsync(id) : await _svc.RemoveAsync(id); }
        catch { r = ActResult.TryLater; }
        if (r == ActResult.Done)
        {
            FriendsSfx.Dismiss();
            if (what == "block") OnBlocked(id, name);
            if (_openId == id) { _openId = null; _picker = null; }
            // The row is gone after the refresh, so the word goes where the player is looking.
            TellOutside(Loc.GetF(what == "block" ? "friends_blocked_done" : "friends_removed_done", name), good: true, always: true);
        }
        else ShowActResult(id, r);
        await SafeRefreshAsync();
        Render();
        return r;
    }

    // ---- requests ---------------------------------------------------------------------

    /// <summary>Accept, Decline or Withdraw a request. Internal for the suite.</summary>
    internal async Task<ActResult> AnswerRequestAsync(FriendRequest r, string what, FrameworkElement? from = null)
    {
        if (_svc == null) return ActResult.TryLater;
        FriendsSfx.Click();
        ActResult res;
        try
        {
            res = what switch
            {
                "accept" => await _svc.AcceptAsync(r.Id),
                "decline" => await _svc.DeclineAsync(r.Id),
                _ => await _svc.CancelRequestAsync(r.Id),
            };
        }
        catch { res = ActResult.TryLater; }
        if (res == ActResult.Done)
        {
            if (what == "accept")
            {
                if (from != null) Shockwave(from, FriendsLook.Mint);
                FriendsSfx.Accepted();
                TellOutside(Loc.Get("friends_add_accepted"), good: true);
            }
            else FriendsSfx.Dismiss();
        }
        else ShowActResult((what == "cancel" ? "out:" : "in:") + r.Id, res);
        await SafeRefreshAsync();
        return res;
    }

    private ContextMenu BuildRequestMenu(FriendRequest r)
    {
        var menu = new ContextMenu
        {
            Background = FriendsLook.Frozen(FriendsLook.Rgb(0x22, 0x16, 0x41)),
            BorderBrush = FriendsLook.Line2Brush,
            Foreground = FriendsLook.TextBrush,
            FontFamily = FriendsLook.Display,
            FontSize = 13.5,
            Tag = "friends-request-menu:" + r.Id,
        };
        AddRequestMenuExtras(menu, r);
        var report = MenuItem("report");
        foreach (var reason in ReportReason.All)
        {
            var sub = MenuItem("report_" + reason);
            var rr = reason;
            sub.Click += async (_, _) => await ReportAsync("in:" + r.Id, r.Id, rr);
            report.Items.Add(sub);
        }
        menu.Items.Add(report);
        return menu;
    }

    /// <summary>A report, worded by what came back. <paramref name="rowId"/> is where the words go.</summary>
    internal async Task<ActResult> ReportAsync(string rowId, string accountId, string reason)
    {
        if (_svc == null) return ActResult.TryLater;
        FriendsSfx.Click();
        ActResult r;
        try { r = await _svc.ReportAsync(accountId, reason); }
        catch { r = ActResult.TryLater; }
        if (r == ActResult.Done) ShowTimed(rowId, Loc.Get("friends_report_done"), true, TimeSpan.FromSeconds(FriendsDrawerRules.ResultHoldSeconds));
        else ShowActResult(rowId, r);
        return r;
    }

    // ---- words ------------------------------------------------------------------------

    private void ShowActResult(string rowId, ActResult r)
    {
        FriendsSfx.Denied();
        ShowTimed(rowId, Loc.Get(FriendsDrawerRules.ActResultKey(r)), false, TimeSpan.FromSeconds(FriendsDrawerRules.ResultHoldSeconds));
    }

    /// <summary>Test hook: where a word goes when the drawer cannot show it. The app throws it as a
    /// floating word over whatever has the screen.</summary>
    internal static Action<string, bool> Outside { get; set; } = (text, good) => FriendsLanding.Tell(text, good);

    /// <summary>Says <paramref name="text"/> outside the drawer when the drawer folded while the
    /// call was out (a game launched and took the screen), or <paramref name="always"/> when the
    /// row that would have carried it is gone (a removed friend).</summary>
    private void TellOutside(string text, bool good, bool always = false)
    {
        if (_isOpen && !always) return;
        try { Outside(text, good); }
        catch (Exception ex) { App.Logger?.Debug("[Friends] tell: {E}", ex.Message); }
    }

    // ---- hooks for the confirm step and the Blocked list (FriendsDrawer.Blocked.cs) -----

    /// <summary>A block went through: remember it for the Blocked list.</summary>
    partial void OnBlocked(string id, string name);

    /// <summary>Remove or Block from a menu: set <paramref name="asked"/> when the card asks first.</summary>
    partial void AskFirst(Friend f, string what, ref bool asked);

    /// <summary>The card of a friend being asked about: add the question, set <paramref name="asking"/>.</summary>
    partial void AddCardAsk(Friend f, StackPanel card, ref bool asking);

    /// <summary>The drawer folded: drop a pending question.</summary>
    partial void ForgetAsk();

    /// <summary>A fresh list: drop a question about someone no longer on it.</summary>
    partial void DropStaleAsk(FriendsSnapshot snap);

    /// <summary>Anything drawn under the friends (the Blocked list); set when it is showing.</summary>
    partial void ListExtrasShowing(ref bool showing);

    partial void AddListExtras();

    /// <summary>Extra buttons in the foot, after the bell.</summary>
    partial void AddFootExtras(StackPanel left);

    /// <summary>Extra lines at the top of a request's menu (Block).</summary>
    partial void AddRequestMenuExtras(ContextMenu menu, FriendRequest r);
}
