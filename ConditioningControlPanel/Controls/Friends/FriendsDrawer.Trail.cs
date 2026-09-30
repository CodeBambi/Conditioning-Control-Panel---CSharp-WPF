using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// The sender's side of receipts (FRIENDS-RECEIPTS v1): under a friend's name, the latest poke,
/// invite or watch you sent them walks sent, arrived, seen, then for an invite the answer (joined,
/// not now) or "no answer" when it ran out. One small line of pips and a word; the service keeps
/// the trail (<see cref="IFriendsService.LastSentTo"/>) and says when one moved.
/// </summary>
public sealed partial class FriendsDrawer
{
    private void OnTrailsChanged()
    {
        try { if (_isOpen) Render(); }
        catch (Exception ex) { App.Logger?.Debug("[Friends] trail repaint failed: {E}", ex.Message); }
    }

    /// <summary>The trail line for <paramref name="f"/>, or null when there is nothing recent to
    /// show or a send result is on the row already (it says the same thing for five seconds).</summary>
    private FrameworkElement? TrailLine(Friend f)
    {
        if (_results.ContainsKey(f.Id)) return null;
        SentTrail? t = null;
        try { t = _svc?.LastSentTo(f.Id); } catch { }
        if (t == null) return null;

        var (kindKey, stateKey) = FriendsDrawerRules.TrailKeys(t);
        var tone = ToneBrush(FriendsDrawerRules.ToneOf(t));
        var line = new TextBlock
        {
            FontSize = 10.5,
            Foreground = FriendsLook.MutedBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0),
            Tag = "friends-trail:" + t.State,
            ToolTip = Loc.Get(t.Kind == SendKind.Invite ? "friends_trail_tip_invite" : "friends_trail_tip"),
        };
        line.Inlines.Add(new Run(Loc.Get(kindKey) + "  "));
        for (int i = 0; i < t.Steps; i++)
        {
            bool lit = i < t.Lit;
            line.Inlines.Add(new Run(lit ? "●" : "○")
            {
                FontSize = 8,
                Foreground = lit ? tone : FriendsLook.DimBrush,
                BaselineAlignment = BaselineAlignment.Center,
            });
        }
        line.Inlines.Add(new Run("  " + Loc.Get(stateKey)) { Foreground = tone });
        return line;
    }

    private static Brush ToneBrush(FriendsDrawerRules.TrailTone tone) => tone switch
    {
        FriendsDrawerRules.TrailTone.Seen => FriendsLook.LilacBrush,
        FriendsDrawerRules.TrailTone.Yes => FriendsLook.MintBrush,
        FriendsDrawerRules.TrailTone.No => FriendsLook.GoldBrush,
        FriendsDrawerRules.TrailTone.Quiet => FriendsLook.DimBrush,
        _ => FriendsLook.MutedBrush,
    };

    /// <summary>Subscribes to the service the way an open does, without the open's network calls.
    /// The suite's door.</summary>
    internal void Listen() => Rebind();

    /// <summary>The trail line's words showing for a friend right now, for the suite.</summary>
    internal string? TrailTextFor(string friendId)
    {
        if (!_rows.TryGetValue(friendId, out var row)) return null;
        var found = FindTagged(row, "friends-trail:");
        if (found is not TextBlock tb) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var inline in tb.Inlines) if (inline is Run r) sb.Append(r.Text);
        return sb.ToString();
    }

    private static FrameworkElement? FindTagged(DependencyObject root, string prefix)
    {
        if (root is FrameworkElement fe && fe.Tag is string s && s.StartsWith(prefix, StringComparison.Ordinal)) return fe;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d && FindTagged(d, prefix) is { } hit) return hit;
        return null;
    }
}
