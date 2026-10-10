using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// The header's presence switch. The line under your name used to be a label that said
/// "friends see what you're playing" or "hidden from friends" and could not be changed after
/// the one-time ask; it is now the switch itself. One click flips it, the service pushes the
/// change on the next poll at once, and the ask pill never comes back after either answer.
/// The same setting lives in Settings, Account, Data and privacy.
/// </summary>
public sealed partial class FriendsDrawer
{
    /// <summary>The avatar dot for you: null signed out, mint while shared, grey while hidden.</summary>
    private bool? PresenceDot()
    {
        try
        {
            if (_svc?.Available != true) return null;
            return _svc.PresenceShared;
        }
        catch { return null; }
    }

    /// <summary>The status line: a plain muted label signed out, else a small pill that flips
    /// presence. Tagged <c>friends-me-status</c> either way.</summary>
    private FrameworkElement PresenceStatus()
    {
        if (_svc?.Available != true)
        {
            // A short status, not the sign-in block's sentence: the page and the drawer both show
            // that block below, and the same line twice read as a stutter (nav polish 2026-10-06).
            var off = FriendsLook.Label(Loc.Get("friends_me_signed_out"), 12, FriendsLook.MutedBrush);
            off.Tag = "friends-me-status";
            return off;
        }

        bool shared;
        try { shared = _svc.PresenceShared; } catch { shared = false; }

        var dot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = shared ? FriendsLook.MintBrush : FriendsLook.OfflineDotBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var words = FriendsLook.Label(Loc.Get(shared ? "friends_me_sharing" : "friends_me_hidden"), 12,
            shared ? FriendsLook.MintBrush : FriendsLook.MutedBrush);
        words.Tag = "friends-me-status-text";
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(words);

        var pill = FriendsLook.Pill(row, Brushes.Transparent, FriendsLook.MutedBrush, FriendsLook.LineBrush, 9,
            new Thickness(7, 2, 8, 2));
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        pill.Margin = new Thickness(0, 3, 0, 0);
        pill.Tag = "friends-me-status";
        pill.ToolTip = Loc.Get("friends_presence_toggle_tip");
        pill.Click += (_, _) => TogglePresence();
        return pill;
    }

    /// <summary>The header block, for the suite.</summary>
    internal FrameworkElement Head => _head;

    /// <summary>Flips presence from the header. Answering here counts as answering the ask.</summary>
    internal void TogglePresence()
    {
        if (_svc?.Available != true) return;
        var next = !FriendsPresenceSetting.Read(_svc, () => false);
        FriendsPresenceSetting.Write(next, _svc, _ => { }, () => PresenceAsk.MarkAsked());
        Render();
    }
}
