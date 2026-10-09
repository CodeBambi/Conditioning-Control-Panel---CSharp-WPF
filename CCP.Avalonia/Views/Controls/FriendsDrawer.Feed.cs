// PORTED from ConditioningControlPanel/Controls/Friends/FriendsDrawer.Feed.cs: "WHAT HAPPENED", the
// friends feed (FriendsFeed) at the top of the list, under the leash. Newest first, one short line
// each with how long ago. Folded it shows a few lines and every new one; "more" shows further lines.
// A line shown in the open drawer is read: it keeps its pink dot until the drawer folds, and the
// chip badge drops it at once. Nothing to tell, no section at all. One persistent panel, refilled on
// its own when a line lands while the drawer is open, so a feed line never rebuilds the list.
// ponytail: the new line's slide-in (LandLine) and the dot's glow are not drawn here.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Friends.Feed;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    /// <summary>The feed this drawer draws: the app's own. Swappable for the suite.</summary>
    internal static Func<FriendsFeed?> FeedSource { get; set; } = () => Friends.FriendsFeedHost.Feed;

    private static readonly IBrush Hover = new SolidColorBrush(Color.FromArgb(0x22, 0xB9, 0x9C, 0xFF));
    private readonly StackPanel _feedBox = new() { Tag = "friends-feed", IsVisible = false };
    private readonly HashSet<string> _feedFresh = new(StringComparer.Ordinal);
    private FriendsFeed? _feedLive;
    private bool _feedMarking;
    private int _feedExtra;

    private void AddFeed()
    {
        _list.Children.Add(_feedBox);
        SafeFillFeed();
    }

    /// <summary>The keys of the lines drawn, top to bottom. The suite reads it.</summary>
    internal IReadOnlyList<string> FeedLineKeys => _feedBox.Children.OfType<Control>()
        .Select(e => e.Tag as string)
        .Where(t => t != null && t.StartsWith("friends-feed-line:", StringComparison.Ordinal))
        .Select(t => t!.Substring("friends-feed-line:".Length))
        .ToList();

    internal Control FeedSection => _feedBox;

    internal void ShowMoreFeed() { _feedExtra += FriendsFeedRules.MoreStep; SafeFillFeed(); }
    internal void ShowLessFeed() { _feedExtra = 0; SafeFillFeed(); }

    private void SafeFillFeed()
    {
        try { FillFeed(); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] feed paint failed: {E}", ex.Message); }
    }

    /// <summary>The drawer folded: the next open starts folded, and what was new this time is not.</summary>
    internal void FeedFolded()
    {
        _feedFresh.Clear();
        _feedExtra = 0;
        Listen(null);
    }

    private static FriendsFeed? CurrentFeed() { try { return FeedSource(); } catch { return null; } }

    private void Listen(FriendsFeed? feed)
    {
        var want = _isOpen ? feed : null;
        if (ReferenceEquals(want, _feedLive)) return;
        if (_feedLive != null) _feedLive.Changed -= OnFeedChanged;
        _feedLive = want;
        if (_feedLive != null) _feedLive.Changed += OnFeedChanged;
    }

    private void OnFeedChanged()
    {
        if (_feedMarking) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnFeedChanged); return; }
        if (!_isOpen) { Listen(null); return; }
        if (!_list.Children.Contains(_feedBox)) return;
        SafeFillFeed();
    }

    private bool IsNew(FeedEntry l) => !l.Read || _feedFresh.Contains(l.Key);

    private void FillFeed()
    {
        _feedBox.Children.Clear();
        var feed = CurrentFeed();
        Listen(feed);
        var lines = feed?.Lines ?? Array.Empty<FeedEntry>();
        if (lines.Count == 0) { _feedBox.IsVisible = false; return; }
        _feedBox.IsVisible = true;

        var fresh = lines.Count(IsNew);
        var show = Math.Min(lines.Count, FriendsFeedRules.FoldCount(lines.Count, fresh) + _feedExtra);
        _feedBox.Children.Add(FeedHead(fresh));
        var now = DateTime.UtcNow;
        IReadOnlyList<Friend> friends = Array.Empty<Friend>();
        try { if (_svc?.Available == true) friends = _svc.Snapshot?.Friends ?? friends; } catch { }
        var shownUnread = new List<string>();
        for (var i = 0; i < show; i++)
        {
            var l = lines[i];
            var row = FeedRow(l.Event, IsNew(l), friends, now);
            if (friends.Any(f => f.Id == l.Event.FriendId)) MakeOpener(row, l.Event.FriendId);
            _feedBox.Children.Add(row);
            if (!l.Read) shownUnread.Add(l.Key);
        }
        var hidden = lines.Count - show;
        if (hidden > 0 || _feedExtra > 0)
        {
            var foot = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 8, 2) };
            if (hidden > 0)
            {
                var hiddenNew = lines.Skip(show).Count(IsNew);
                var more = FeedLink(hiddenNew > 0 ? Loc.GetF("friends_feed_more_new", hidden, hiddenNew) : Loc.GetF("friends_feed_more", hidden), "friends-feed-more");
                more.Click += (_, _) => { Friends.FriendsSfx.Click(); ShowMoreFeed(); };
                foot.Children.Add(more);
            }
            if (_feedExtra > 0)
            {
                var less = FeedLink(Loc.Get("friends_feed_less"), "friends-feed-less");
                less.Click += (_, _) => ShowLessFeed();
                foot.Children.Add(less);
            }
            _feedBox.Children.Add(foot);
        }
        // Shown in an open drawer = read. A drawer built but never opened reads nothing.
        if (_isOpen && feed != null && shownUnread.Count > 0)
        {
            foreach (var k in shownUnread) _feedFresh.Add(k);
            _feedMarking = true;
            try { feed.MarkRead(shownUnread); }
            finally { _feedMarking = false; }
        }
    }

    private static Control FeedHead(int fresh)
    {
        var g = new Grid { Margin = new Thickness(8, 10, 8, 4), Tag = "friends-feed-head" };
        g.Children.Add(Tagged(Label(Loc.Get("friends_feed_title"), 10, Dim, Mono), "friends-feed-title"));
        if (fresh > 0)
        {
            var n = Tagged(Label(Loc.GetF("friends_feed_new", fresh), 10, Pink, Mono), "friends-feed-count");
            n.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(n);
        }
        return g;
    }

    private static Border FeedRow(FriendEvent e, bool isNew, IReadOnlyList<Friend> friends, DateTime nowUtc)
    {
        var g = new Grid { MinHeight = 22, ColumnDefinitions = new ColumnDefinitions("10,*,Auto") };
        var row = new Border
        {
            Margin = new Thickness(4, 0), Padding = new Thickness(4, 0), CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent, Tag = "friends-feed-line:" + e.Key, Child = g,
        };
        if (isNew)
            g.Children.Add(new Ellipse
            {
                Width = 6, Height = 6, Fill = Pink, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Tag = "friends-feed-new",
            });
        var text = new TextBlock
        {
            FontSize = 12, Foreground = isNew ? Text : Muted, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Tag = "friends-feed-text",
        };
        var plain = FillLine(text, e, friends, isNew);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var (key, n) = FriendsFeedRules.Ago(e.AtUtc, nowUtc);
        var ago = Tagged(Label(n is int x ? Loc.GetF(key, x) : Loc.Get(key), 10, Dim, Mono), "friends-feed-ago");
        ago.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(ago, 2);
        g.Children.Add(ago);
        // A long name trims; the whole line is one hover away.
        ToolTip.SetTip(row, plain);
        return row;
    }

    /// <summary>Fills <paramref name="t"/> with the line, the name in its own weight, and returns it as plain text.</summary>
    internal static string FillLine(TextBlock t, FriendEvent e, IReadOnlyList<Friend> friends, bool isNew)
    {
        var line = FriendsFeedRules.Describe(e);
        var name = FriendsFeedRules.NameFor(e, friends) ?? Loc.Get("friends_feed_someone");
        var detail = line.Detail == null ? "" : line.DetailIsKey ? Loc.Get(line.Detail) : line.Detail;
        // Split on the name first, so a watch title that happens to hold "{0}" stays a title.
        var parts = Loc.Get(line.Key).Split("{0}");
        var plain = new System.Text.StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Replace("{1}", detail);
            if (part.Length > 0) { t.Inlines!.Add(new Run(part)); plain.Append(part); }
            if (i < parts.Length - 1)
            {
                t.Inlines!.Add(new Run(name) { FontFamily = Display, FontWeight = FontWeight.SemiBold, Foreground = isNew ? Text : Lilac });
                plain.Append(name);
            }
        }
        return plain.ToString();
    }

    /// <summary>A line about someone on the list opens their card (Poke back is one press away).</summary>
    private void MakeOpener(Border row, string friendId)
    {
        row.Cursor = Hand();
        row.PointerEntered += (_, _) => row.Background = Hover;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            OpenFromFeed(friendId);
        };
    }

    /// <summary>Opens <paramref name="friendId"/>'s card and scrolls it into view.</summary>
    internal void OpenFromFeed(string friendId)
    {
        try
        {
            (_openId, _confirm, _picker) = (friendId, null, null);
            Render();
            foreach (var c in _list.Children)
                if (c.Tag as string == "friends-row:" + friendId) { c.BringIntoView(); break; }
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] feed open failed: {E}", ex.Message); }
    }

    private static Button FeedLink(string text, string tag)
    {
        var b = Pill(text, Brushes.Transparent, Lilac, tag);
        b.Padding = new Thickness(6, 2, 6, 2);
        if (b.Content is TextBlock tb) tb.FontSize = 11.5;
        return b;
    }
}
