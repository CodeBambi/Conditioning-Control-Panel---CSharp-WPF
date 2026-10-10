// PORTED from ConditioningControlPanel/Controls/Friends/FriendsDrawer.Trail.cs: the sender's side of
// receipts (FRIENDS-RECEIPTS v1). Under a friend's name, the latest poke, invite or watch you sent
// them walks sent, arrived, seen, then for an invite the answer, or "no answer" when it ran out. One
// small line of pips and a word; the service keeps the trail (LastSentTo) and says when one moved.
// Also the row's lock-day chip (WPF BuildFriendRow, Presence.LockDay).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    private void OnTrailsChanged()
    {
        try { if (_isOpen) global::Avalonia.Threading.Dispatcher.UIThread.Post(Render); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] trail repaint failed: {E}", ex.Message); }
    }

    /// <summary>The trail line for <paramref name="f"/>, or null when there is nothing recent or a send
    /// result is on the row already (it says the same thing for a few seconds).</summary>
    private TextBlock? TrailLine(Friend f)
    {
        if (_results.ContainsKey(f.Id)) return null;
        SentTrail? t = null;
        try { t = _svc?.LastSentTo(f.Id); } catch { }
        if (t == null) return null;
        var (kindKey, stateKey) = FriendsDrawerRules.TrailKeys(t);
        var tone = ToneBrush(FriendsDrawerRules.ToneOf(t));
        var line = new TextBlock
        {
            FontSize = 10.5, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0), Tag = "friends-trail:" + t.State,
        };
        ToolTip.SetTip(line, Loc.Get(t.Kind == SendKind.Invite ? "friends_trail_tip_invite" : "friends_trail_tip"));
        line.Inlines!.Add(new Run(Loc.Get(kindKey) + "  "));
        for (int i = 0; i < t.Steps; i++)
        {
            bool lit = i < t.Lit;
            line.Inlines.Add(new Run(lit ? "●" : "○") { FontSize = 8, Foreground = lit ? tone : Dim, BaselineAlignment = BaselineAlignment.Center });
        }
        line.Inlines.Add(new Run("  " + Loc.Get(stateKey)) { Foreground = tone });
        return line;
    }

    private static IBrush ToneBrush(FriendsDrawerRules.TrailTone tone) => tone switch
    {
        FriendsDrawerRules.TrailTone.Seen => Lilac,
        FriendsDrawerRules.TrailTone.Yes => Mint,
        FriendsDrawerRules.TrailTone.No => Gold,
        FriendsDrawerRules.TrailTone.Quiet => Dim,
        _ => Muted,
    };

    /// <summary>WPF BuildFriendRow's lock chip: a friend in a Chaster lock shows the day.</summary>
    private static Control? LockChip(Friend f) => f.Presence.LockDay is int day
        ? new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0x5F, 0xB4)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x5F, 0xB4)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 1),
            VerticalAlignment = VerticalAlignment.Center, Tag = "friends-lock",
            Child = Label("\U0001F512 " + Loc.GetF("friends_lock_day", day), 10, Pink, Mono),
        }
        : null;
}
