using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// THE FRIENDS DRAWER: the Hearthstone shape. You sit bottom-left (the rail chip), the list
/// opens upward, online first, then offline, then requests. Click a friend and the card opens
/// with the four actions in the owner's order (Invite, Poke, Send a watch, more); right-click is
/// the same menu "more" opens. The foot holds Settings, Add friend and your own code.
///
/// <para>Code-built, one instance per host (the panel's rail chip and the launcher's chip each
/// own one). It talks only to <see cref="IFriendsService"/>: <c>App.Friends</c> in the app, a fake
/// in the suite. Every service call is guarded; the service can be null until the core lane
/// lands, and the drawer then draws its signed-out face.</para>
///
/// <para>Every action is a preset. There is no text box that crosses the wire except the
/// friend code and the HT video number, and both are filtered to their grammar as typed.</para>
/// </summary>
public sealed partial class FriendsDrawer : Border
{
    public const double DrawerWidth = 300;
    public const double DrawerMaxHeight = 548;

    private IFriendsService? _svc;
    private readonly Func<IFriendsService?> _resolve;

    private readonly Grid _root = new();
    private readonly Border _head = new();
    private readonly Border _ask = new();
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroll = new();
    private readonly Border _addBox = new();
    private readonly Border _foot = new();
    private readonly Canvas _fx = new() { IsHitTestVisible = false };

    private TextBox? _codeBox;
    private TextBlock? _addResult;
    private Button? _addGo;
    private TextBlock? _copied;

    /// <summary>The friend whose card is open, or null.</summary>
    private string? _openId;

    /// <summary>The picker open inside that card: "poke", "invite", "watch" or null.</summary>
    private string? _picker;

    private string _watchTab = "flavour";

    /// <summary>Per friend, the worded result of the last send, shown for two seconds.</summary>
    private readonly Dictionary<string, (string Text, bool Good)> _results = new();
    private readonly Dictionary<string, DispatcherTimer> _resultTimers = new();

    /// <summary>Row element per friend or request id, rebuilt on every render. The juice and
    /// the suite both find rows through it.</summary>
    private readonly Dictionary<string, FrameworkElement> _rows = new();
    private readonly Dictionary<string, FrameworkElement> _avatars = new();
    private readonly List<string> _rowOrder = new();
    private readonly List<string> _sectionOrder = new();

    private FriendsSnapshot _last = FriendsSnapshot.Empty;

    /// <summary>Raised when the Settings button is pressed. The host decides where Settings
    /// lives (the panel's tab, or the panel opened from the launcher).</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the drawer wants to fold (Escape inside it, Settings).</summary>
    public event Action? CloseRequested;

    /// <summary>The drawer. <paramref name="service"/> is for the suite; the app passes nothing
    /// and the drawer reads <c>App.Friends</c> each time it opens.</summary>
    public FriendsDrawer(IFriendsService? service = null)
    {
        _resolve = service != null ? () => service : () => App.Friends;
        _svc = _resolve();

        Width = DrawerWidth;
        MaxHeight = DrawerMaxHeight;
        CornerRadius = new CornerRadius(16);
        Background = FriendsLook.GlassBrush;
        BorderBrush = FriendsLook.Line2Brush;
        BorderThickness = new Thickness(1);
        ClipToBounds = false;
        SnapsToDevicePixels = true;
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 30, ShadowDepth = 10, Direction = 270, Opacity = 0.55,
        };
        Focusable = true;
        FocusVisualStyle = null;

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _head.CornerRadius = new CornerRadius(15, 15, 0, 0);
        _head.Background = FriendsLook.HeadBrush;
        _head.BorderBrush = FriendsLook.LineBrush;
        _head.BorderThickness = new Thickness(0, 0, 0, 1);
        _head.Padding = new Thickness(12, 12, 12, 10);
        Grid.SetRow(_head, 0);

        _ask.Margin = new Thickness(10, 8, 10, 0);
        Grid.SetRow(_ask, 1);

        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _scroll.Padding = new Thickness(6, 4, 6, 8);
        _scroll.Content = _list;
        _scroll.MinHeight = 120;
        Grid.SetRow(_scroll, 2);

        _addBox.Visibility = Visibility.Collapsed;
        _addBox.Padding = new Thickness(10, 8, 10, 8);
        _addBox.BorderBrush = FriendsLook.LineBrush;
        _addBox.BorderThickness = new Thickness(0, 1, 0, 0);
        Grid.SetRow(_addBox, 3);

        _foot.Background = FriendsLook.FootBrush;
        _foot.BorderBrush = FriendsLook.LineBrush;
        _foot.BorderThickness = new Thickness(0, 1, 0, 0);
        _foot.CornerRadius = new CornerRadius(0, 0, 15, 15);
        _foot.Padding = new Thickness(8);
        Grid.SetRow(_foot, 4);

        Grid.SetRowSpan(_fx, 5);

        _root.Children.Add(_head);
        _root.Children.Add(_ask);
        _root.Children.Add(_scroll);
        _root.Children.Add(_addBox);
        _root.Children.Add(_foot);
        _root.Children.Add(_fx);
        Child = _root;

        PreviewKeyDown += OnKey;
        BuildAddBox();
        Render();
    }

    /// <summary>The service this drawer speaks to right now (re-read on every open, because
    /// App.Friends is rebuilt on sign in and sign out).</summary>
    internal IFriendsService? Service => _svc;

    // ---- open / close, called by the chip ---------------------------------------------

    /// <summary>The chip opened the drawer: re-read the service, subscribe, ask for a fresh
    /// list, draw and play the entrance.</summary>
    public void OnOpened()
    {
        Rebind();
        try { _svc?.SetDrawerOpen(true); } catch { }
        try { if (_svc?.Available == true) _ = SafeRefreshAsync(); } catch { }
        try { if (_svc?.Available == true) StartTables(); } catch { }
        Render();
        PlayEntrance();
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => Focus()));
    }

    /// <summary>The chip folded the drawer. The card closes with it, so the next open starts clean.</summary>
    public void OnClosed()
    {
        try { _svc?.SetDrawerOpen(false); } catch { }
        _openId = null;
        _picker = null;
        _addBox.Visibility = Visibility.Collapsed;
        StopAmbient();
        StopTables();
    }

    private void Rebind()
    {
        var next = _resolve();
        if (ReferenceEquals(next, _svc)) { Subscribe(); return; }
        Unsubscribe();
        _svc = next;
        Subscribe();
    }

    private bool _subscribed;

    private void Subscribe()
    {
        if (_subscribed || _svc == null) return;
        _svc.SnapshotChanged += OnSnapshot;
        _svc.Sent += OnSent;
        _subscribed = true;
    }

    /// <summary>Drops the service events. The chip calls it when its host unloads.</summary>
    public void Unsubscribe()
    {
        if (!_subscribed || _svc == null) { _subscribed = false; return; }
        _svc.SnapshotChanged -= OnSnapshot;
        _svc.Sent -= OnSent;
        _subscribed = false;
        StopTables();
    }

    private async Task SafeRefreshAsync()
    {
        try { if (_svc != null) await _svc.RefreshAsync(); }
        catch (Exception ex) { App.Logger?.Debug("[Friends] refresh failed: {E}", ex.Message); }
    }

    private void OnSnapshot(FriendsSnapshot snap)
    {
        try
        {
            var hop = FriendsDrawerRules.CameOnline(_last, snap);
            Render();
            foreach (var id in hop) HopAvatar(id);
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] repaint failed: {E}", ex.Message); }
    }

    private void OnSent(SendKind kind, Friend friend) { /* the per-row result already says it */ }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (e.OriginalSource is TextBox && _addBox.Visibility == Visibility.Visible && _codeBox?.IsKeyboardFocused == true)
        {
            _addBox.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }
        if (_picker != null) { _picker = null; Render(); e.Handled = true; return; }
        CloseRequested?.Invoke();
        e.Handled = true;
    }

    // ---- drawing ----------------------------------------------------------------------

    /// <summary>Draws the whole drawer from the service's current snapshot. Cheap: the list
    /// holds a few dozen rows at most.</summary>
    internal void Render()
    {
        var snap = FriendsSnapshot.Empty;
        try { if (_svc?.Available == true) snap = _svc.Snapshot ?? FriendsSnapshot.Empty; } catch { }
        _last = snap;

        RenderHead(snap);
        RenderAsk();
        RenderList(snap);
        RenderFoot(snap);
    }

    private void RenderHead(FriendsSnapshot snap)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = MeName();
        var avatar = FriendsLook.Avatar(name, null, 40, _svc?.Available == true ? true : null);
        avatar.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(avatar);

        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(FriendsLook.Label(name, 16, FriendsLook.TextBrush, FriendsLook.Display, FontWeights.SemiBold));
        if (FriendsLook.TierPlate(MeTier(), 15) is { } plate) line.Children.Add(plate);
        who.Children.Add(line);

        string status;
        if (_svc?.Available != true) status = Loc.Get("friends_signed_out");
        else if (_svc.PresenceShared) status = Loc.Get("friends_me_sharing");
        else status = Loc.Get("friends_me_hidden");
        var st = FriendsLook.Label(status, 12, FriendsLook.MutedBrush);
        st.Tag = "friends-me-status";
        who.Children.Add(st);
        Grid.SetColumn(who, 1);
        g.Children.Add(who);

        _head.Child = g;
    }

    private void RenderAsk()
    {
        _ask.Child = null;
        _ask.Visibility = Visibility.Collapsed;
        if (_svc?.Available != true) return;
        bool shared;
        try { shared = _svc.PresenceShared; } catch { shared = false; }
        if (shared || PresenceAsk.Asked()) return;

        var pill = new Border
        {
            Background = FriendsLook.Frozen(Color.FromArgb(0x1F, 0x5F, 0xFF, 0xD0)),
            BorderBrush = FriendsLook.Frozen(Color.FromArgb(0x66, 0x5F, 0xFF, 0xD0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 6, 6, 6),
        };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var q = FriendsLook.Label(Loc.Get("friends_presence_ask"), 12.5, FriendsLook.TextBrush, null, FontWeights.SemiBold);
        q.TextWrapping = TextWrapping.Wrap;
        q.TextTrimming = TextTrimming.None;
        words.Children.Add(q);
        var sub = FriendsLook.Label(Loc.Get("friends_presence_ask_sub"), 11, FriendsLook.MutedBrush);
        sub.TextWrapping = TextWrapping.Wrap;
        sub.TextTrimming = TextTrimming.None;
        sub.Margin = new Thickness(0, 2, 0, 0);
        words.Children.Add(sub);
        g.Children.Add(words);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        var yes = FriendsLook.Pill(Loc.Get("friends_presence_yes"), FriendsLook.MintBrush, FriendsLook.MintInkBrush,
            FriendsLook.MintBrush, 8, new Thickness(9, 3, 9, 3), FriendsLook.MintBrush);
        yes.FontSize = 12;
        yes.Tag = "friends-presence-yes";
        yes.Click += (_, _) =>
        {
            try { if (_svc != null) _svc.PresenceShared = true; } catch { }
            PresenceAsk.MarkAsked();
            Render();
        };
        var no = FriendsLook.Pill(Loc.Get("friends_presence_no"), FriendsLook.RaisedBrush, FriendsLook.TextBrush,
            FriendsLook.Line2Brush, 8, new Thickness(9, 3, 9, 3));
        no.FontSize = 12;
        no.Margin = new Thickness(6, 0, 0, 0);
        no.Tag = "friends-presence-no";
        no.Click += (_, _) => { PresenceAsk.MarkAsked(); Render(); };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        Grid.SetColumn(buttons, 1);
        g.Children.Add(buttons);
        pill.Child = g;
        _ask.Child = pill;
        _ask.Visibility = Visibility.Visible;
    }

    private void RenderList(FriendsSnapshot snap)
    {
        StopAmbient();
        _list.Children.Clear();
        _rows.Clear();
        _avatars.Clear();
        _rowOrder.Clear();
        _sectionOrder.Clear();

        if (_svc?.Available != true)
        {
            _list.Children.Add(SignInBlock());
            return;
        }

        var (online, offline) = FriendsDrawerRules.Split(snap);
        (online, offline) = FriendsDrawerRules.HostingFirst(online, offline, f => TableFor(f) != null);
        if (_openId != null && !ContainsFriend(snap, _openId)) { _openId = null; _picker = null; }

        if (online.Count > 0)
        {
            AddSection("friends_section_online", online.Count);
            foreach (var f in online) AddRow(f.Id, BuildFriendRow(f));
        }
        if (offline.Count > 0)
        {
            AddSection("friends_section_offline", offline.Count);
            foreach (var f in offline) AddRow(f.Id, BuildFriendRow(f));
        }
        int req = snap.Incoming.Count + snap.Outgoing.Count;
        if (req > 0)
        {
            AddSection("friends_section_requests", req);
            foreach (var r in snap.Incoming) AddRow("in:" + r.Id, BuildRequestRow(r, incoming: true));
            foreach (var r in snap.Outgoing) AddRow("out:" + r.Id, BuildRequestRow(r, incoming: false));
        }
        if (online.Count + offline.Count + req == 0)
            _list.Children.Add(EmptyLine(Loc.Get("friends_empty")));

        StartAmbient();
    }

    private static bool ContainsFriend(FriendsSnapshot snap, string id)
    {
        foreach (var f in snap.Friends) if (f.Id == id) return true;
        return false;
    }

    private void AddSection(string key, int count)
    {
        _sectionOrder.Add(key);
        _list.Children.Add(FriendsLook.SectionHead(Loc.Get(key), count));
    }

    private void AddRow(string id, FrameworkElement row)
    {
        _rows[id] = row;
        _rowOrder.Add(id);
        _list.Children.Add(row);
    }

    /// <summary>Signed out: one short line and a real button into the app's sign-in dialog.</summary>
    private FrameworkElement SignInBlock()
    {
        var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(18, 28, 18, 28) };
        var line = EmptyLine(Loc.Get("friends_signed_out"));
        line.Margin = new Thickness(0, 0, 0, 12);
        box.Children.Add(line);
        var btn = FriendsLook.Pill(Loc.Get("friends_sign_in"), FriendsLook.MintBrush, FriendsLook.MintInkBrush,
            FriendsLook.MintBrush, 10, new Thickness(16, 6, 16, 6), FriendsLook.MintBrush);
        btn.FontSize = 13;
        btn.HorizontalAlignment = HorizontalAlignment.Center;
        btn.Tag = "friends-sign-in";
        btn.Click += (_, _) =>
        {
            // MainWindowRef, not Application.Current.MainWindow: that can be the launcher or null in the tray.
            (App.MainWindowRef ?? Application.Current?.MainWindow as MainWindow)
                ?.OpenUnifiedLoginDialog(Window.GetWindow(this));
        };
        box.Children.Add(btn);
        return box;
    }

    private static TextBlock EmptyLine(string text)
    {
        var t = FriendsLook.Label(text, 12.5, FriendsLook.MutedBrush);
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        t.TextAlignment = TextAlignment.Center;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(18, 28, 18, 28);
        t.Tag = "friends-empty";
        return t;
    }

    private FrameworkElement BuildFriendRow(Friend f)
    {
        bool open = _openId == f.Id;
        var table = TableFor(f);
        var row = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 1, 0, 1),
            Background = open ? FriendsLook.RaisedBrush : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Tag = "friends-row:" + f.Id,
            ClipToBounds = true,
        };
        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = FriendsLook.Avatar(f.Name, f.AvatarUrl, 38, f.Online || table != null);
        if (!f.Online && table == null) avatar.Opacity = 0.55;
        avatar.RenderTransformOrigin = new Point(0.5, 1);
        avatar.RenderTransform = new TranslateTransform();
        _avatars[f.Id] = avatar;
        top.Children.Add(avatar);

        var mid = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        var nm = FriendsLook.Label(f.Name, 14, f.Online ? FriendsLook.TextBrush : FriendsLook.MutedBrush,
            FriendsLook.Display, FontWeights.Medium);
        nameLine.Children.Add(nm);
        if (FriendsLook.TierPlate(f.Tier, 12) is { } plate) nameLine.Children.Add(plate);
        if (f.Squelched)
        {
            var sq = FriendsLook.Label(Loc.Get("friends_squelched_tag"), 9, FriendsLook.DimBrush, FriendsLook.Mono);
            sq.Margin = new Thickness(6, 1, 0, 0);
            sq.Tag = "friends-squelched";
            nameLine.Children.Add(sq);
        }
        mid.Children.Add(nameLine);
        mid.Children.Add(table != null ? HostingLine() : ActivityLine(f));
        Grid.SetColumn(mid, 1);
        top.Children.Add(mid);

        if (f.Presence.LockDay is int day)
        {
            var lockChip = new Border
            {
                Background = FriendsLook.Frozen(Color.FromArgb(0x1A, 0xFF, 0x5F, 0xB4)),
                BorderBrush = FriendsLook.Frozen(Color.FromArgb(0x40, 0xFF, 0x5F, 0xB4)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "friends-lock",
                Child = FriendsLook.Label("\U0001F512 " + Loc.GetF("friends_lock_day", day), 10, FriendsLook.PinkBrush, FriendsLook.Mono),
            };
            Grid.SetColumn(lockChip, 2);
            top.Children.Add(lockChip);
        }
        if (table != null)
        {
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var join = TableJoinButton(f, table);
            Grid.SetColumn(join, 3);
            top.Children.Add(join);
            if (!open) DressHostingRow(row);
        }
        outer.Children.Add(top);

        if (open)
        {
            var card = BuildCard(f);
            Grid.SetRow(card, 1);
            outer.Children.Add(card);
        }

        if (_results.TryGetValue(f.Id, out var res))
        {
            var line = FriendsLook.Label(res.Text, 11.5, res.Good ? FriendsLook.MintBrush : FriendsLook.GoldBrush, FriendsLook.Display);
            line.Margin = new Thickness(48, 4, 0, 0);
            line.Tag = "friends-result";
            Grid.SetRow(line, 2);
            outer.Children.Add(line);
        }

        row.Child = outer;
        AttachSheen(row, open);

        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInsideCard(d, row)) return;
            Toggle(f.Id);
            e.Handled = true;
        };
        row.ContextMenu = BuildMenu(f);
        return row;
    }

    private static bool IsInsideCard(DependencyObject d, Border row)
    {
        var cur = d;
        while (cur != null && !ReferenceEquals(cur, row))
        {
            if (cur is FrameworkElement fe && fe.Tag is string s && s == "friends-card") return true;
            cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
        }
        return false;
    }

    private TextBlock ActivityLine(Friend f)
    {
        var t = new TextBlock
        {
            FontSize = 11.5,
            Foreground = FriendsLook.MutedBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Tag = "friends-activity",
        };
        if (f.Online)
        {
            t.Inlines.Add(new System.Windows.Documents.Run("● ") { Foreground = FriendsLook.MintBrush });
            t.Inlines.Add(new System.Windows.Documents.Run(Loc.Get(FriendsDrawerRules.ActivityKey(f.Presence.Activity))));
        }
        else
        {
            var (key, arg) = FriendsDrawerRules.SeenKey(f.Presence.LastSeen, DateTimeOffset.UtcNow);
            t.Text = arg is int n ? Loc.GetF(key, n) : Loc.Get(key);
        }
        return t;
    }

    /// <summary>The expanded card: the four actions in the owner's order, then whichever
    /// picker is open.</summary>
    private FrameworkElement BuildCard(Friend f)
    {
        var card = new StackPanel { Margin = new Thickness(0, 8, 0, 2), Tag = "friends-card" };
        var grid = new UniformGrid { Columns = 2 };
        foreach (var act in FriendsDrawerRules.CardActions)
        {
            if (act == "more") continue;
            var b = ActionButton(act, f);
            b.Margin = new Thickness(0, 0, grid.Children.Count % 2 == 0 ? 3 : 0, 6);
            if (grid.Children.Count % 2 == 1) b.Margin = new Thickness(3, 0, 0, 6);
            grid.Children.Add(b);
        }
        card.Children.Add(grid);

        var more = FriendsLook.Pill(ButtonContent("", Loc.Get("friends_action_more"), center: true),
            Brushes.Transparent, FriendsLook.MutedBrush, FriendsLook.Line2Brush, 10, new Thickness(10, 6, 10, 6));
        more.Tag = "friends-action:more";
        more.Click += (_, _) =>
        {
            var menu = BuildMenu(f);
            menu.PlacementTarget = more;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        card.Children.Add(more);

        if (_picker != null)
        {
            var picker = BuildPicker(f, _picker);
            picker.Margin = new Thickness(0, 8, 0, 0);
            card.Children.Add(picker);
        }
        return card;
    }

    private Button ActionButton(string act, Friend f)
    {
        string glyph = act switch { "invite" => "", "poke" => "", _ => "" };
        string key = act switch { "invite" => "friends_action_invite", "poke" => "friends_action_poke", _ => "friends_action_watch" };
        bool lit = _picker == act;
        var b = FriendsLook.Pill(ButtonContent(glyph, Loc.Get(key), center: false),
            lit ? FriendsLook.ButtonHoverBrush : FriendsLook.ButtonBrush, FriendsLook.TextBrush,
            lit ? FriendsLook.LilacBrush : FriendsLook.Line2Brush, 10, new Thickness(10, 8, 10, 8));
        b.Tag = "friends-action:" + act;
        b.Click += (_, _) => { _picker = _picker == act ? null : act; Render(); };
        return b;
    }

    private static StackPanel ButtonContent(string glyph, string text, bool center)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        };
        sp.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = FriendsLook.LilacBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        sp.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = FriendsLook.Display,
            FontWeight = FontWeights.Medium,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return sp;
    }

    private FrameworkElement BuildRequestRow(FriendRequest r, bool incoming)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 1, 0, 1),
            Background = Brushes.Transparent,
            Tag = (incoming ? "friends-request-in:" : "friends-request-out:") + r.Id,
        };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = FriendsLook.Avatar(r.Name, r.AvatarUrl, 38);
        g.Children.Add(avatar);

        var mid = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(FriendsLook.Label(r.Name, 14, FriendsLook.TextBrush, FriendsLook.Display, FontWeights.Medium));
        string sub = !incoming ? Loc.Get("friends_request_waiting")
            : !string.IsNullOrEmpty(r.Via) ? Loc.GetF("friends_request_via", r.Via!)
            : Loc.Get("friends_request_new");
        mid.Children.Add(FriendsLook.Label(sub, 11.5, FriendsLook.MutedBrush));
        Grid.SetColumn(mid, 1);
        g.Children.Add(mid);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (incoming)
        {
            var add = FriendsLook.Pill(Loc.Get("friends_request_add"), FriendsLook.MintBrush, FriendsLook.MintInkBrush,
                FriendsLook.MintBrush, 8, new Thickness(9, 3, 9, 3), FriendsLook.MintBrush);
            add.FontSize = 12;
            add.Tag = "friends-accept";
            add.Click += async (_, _) =>
            {
                Shockwave(add, FriendsLook.Mint);
                try { if (_svc != null) await _svc.AcceptAsync(r.Id); } catch { }
                await SafeRefreshAsync();
            };
            var no = FriendsLook.Pill(Loc.Get("friends_request_no"), FriendsLook.RaisedBrush, FriendsLook.TextBrush,
                FriendsLook.Line2Brush, 8, new Thickness(9, 3, 9, 3));
            no.FontSize = 12;
            no.Margin = new Thickness(6, 0, 0, 0);
            no.Tag = "friends-decline";
            no.Click += async (_, _) =>
            {
                try { if (_svc != null) await _svc.DeclineAsync(r.Id); } catch { }
                await SafeRefreshAsync();
            };
            buttons.Children.Add(add);
            buttons.Children.Add(no);
        }
        else
        {
            var cancel = FriendsLook.Pill(Loc.Get("friends_request_cancel"), FriendsLook.RaisedBrush, FriendsLook.MutedBrush,
                FriendsLook.Line2Brush, 8, new Thickness(9, 3, 9, 3));
            cancel.FontSize = 12;
            cancel.Tag = "friends-cancel";
            cancel.Click += async (_, _) =>
            {
                try { if (_svc != null) await _svc.CancelRequestAsync(r.Id); } catch { }
                await SafeRefreshAsync();
            };
            buttons.Children.Add(cancel);
        }
        Grid.SetColumn(buttons, 2);
        g.Children.Add(buttons);
        row.Child = g;
        AttachSheen(row, false);
        return row;
    }

    private void RenderFoot(FriendsSnapshot snap)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var settings = FootButton("", Loc.Get("friends_settings"));
        settings.Tag = "friends-settings";
        settings.Click += (_, _) => { SettingsRequested?.Invoke(); CloseRequested?.Invoke(); };
        g.Children.Add(settings);

        var add = FootButton("", Loc.Get("friends_add_title"));
        add.Tag = "friends-add-open";
        add.IsEnabled = _svc?.Available == true;
        add.Click += (_, _) =>
        {
            bool show = _addBox.Visibility != Visibility.Visible;
            _addBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show)
            {
                if (_addResult != null) _addResult.Text = "";
                _codeBox?.Focus();
                MotionFx.StaggerIn(new FrameworkElement[] { _addBox });
            }
        };
        Grid.SetColumn(add, 1);
        g.Children.Add(add);

        var code = string.IsNullOrEmpty(snap.MyCode) ? "" : snap.MyCode;
        if (code.Length > 0)
        {
            var mine = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var codeText = FriendsLook.Label(code, 12, FriendsLook.LilacBrush, FriendsLook.Mono, FontWeights.SemiBold);
            codeText.Tag = "friends-my-code";
            top.Children.Add(codeText);
            var copy = FriendsLook.Pill(new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = FriendsLook.MutedBrush,
            }, Brushes.Transparent, FriendsLook.MutedBrush, Brushes.Transparent, 6, new Thickness(5, 3, 5, 3));
            copy.Margin = new Thickness(4, 0, 0, 0);
            copy.Tag = "friends-copy";
            copy.ToolTip = Loc.Get("friends_copy");
            copy.Click += (_, _) => CopyCode(code, copy);
            top.Children.Add(copy);
            mine.Children.Add(top);
            _copied = FriendsLook.Label(Loc.Get("friends_my_code"), 9, FriendsLook.DimBrush, FriendsLook.Mono);
            _copied.HorizontalAlignment = HorizontalAlignment.Right;
            mine.Children.Add(_copied);
            Grid.SetColumn(mine, 2);
            g.Children.Add(mine);
        }
        _foot.Child = g;
    }

    private static Button FootButton(string glyph, string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        sp.Children.Add(new TextBlock { Text = text, FontFamily = FriendsLook.Display, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        return FriendsLook.Pill(sp, Brushes.Transparent, FriendsLook.MutedBrush, Brushes.Transparent, 9,
            new Thickness(8, 6, 8, 6), FriendsLook.HoverBrush);
    }

    private void CopyCode(string code, FrameworkElement from)
    {
        try { Clipboard.SetText(code); } catch { return; }
        if (_copied != null)
        {
            _copied.Text = Loc.Get("friends_copied");
            _copied.Foreground = FriendsLook.MintBrush;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (_copied == null) return;
                _copied.Text = Loc.Get("friends_my_code");
                _copied.Foreground = FriendsLook.DimBrush;
            };
            t.Start();
        }
        Pop(from, FriendsLook.Lilac);
    }

    // ---- add by code ------------------------------------------------------------------

    private void BuildAddBox()
    {
        var sp = new StackPanel();
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var prefix = FriendsLook.Label(FriendsDrawerRules.CodePrefix, 14, FriendsLook.DimBrush, FriendsLook.Mono);
        prefix.Margin = new Thickness(0, 0, 4, 0);
        g.Children.Add(prefix);

        _codeBox = new TextBox
        {
            MaxLength = 9,
            CharacterCasing = CharacterCasing.Upper,
            FontFamily = FriendsLook.Mono,
            FontSize = 14,
            Foreground = FriendsLook.TextBrush,
            Background = FriendsLook.Frozen(FriendsLook.Ground),
            BorderBrush = FriendsLook.Line2Brush,
            CaretBrush = FriendsLook.LilacBrush,
            Padding = new Thickness(6, 3, 6, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = Loc.Get("friends_add_hint"),
            Tag = "friends-code-box",
        };
        _codeBox.TextChanged += (_, _) =>
        {
            var n = FriendsDrawerRules.NormaliseCode(_codeBox.Text);
            if (n != _codeBox.Text)
            {
                _codeBox.Text = n;
                _codeBox.CaretIndex = n.Length;
            }
            if (_addGo != null) _addGo.IsEnabled = n.Length == FriendsDrawerRules.CodeLength;
        };
        _codeBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _addGo?.IsEnabled == true) { _ = AddByCodeAsync(); e.Handled = true; }
        };
        Grid.SetColumn(_codeBox, 1);
        g.Children.Add(_codeBox);

        _addGo = FriendsLook.Pill(Loc.Get("friends_add_go"), FriendsLook.MintBrush, FriendsLook.MintInkBrush,
            FriendsLook.MintBrush, 8, new Thickness(12, 4, 12, 4), FriendsLook.MintBrush);
        _addGo.Margin = new Thickness(6, 0, 0, 0);
        _addGo.IsEnabled = false;
        _addGo.Tag = "friends-add-go";
        _addGo.Click += (_, _) => _ = AddByCodeAsync();
        Grid.SetColumn(_addGo, 2);
        g.Children.Add(_addGo);
        sp.Children.Add(g);

        _addResult = FriendsLook.Label("", 11.5, FriendsLook.MutedBrush, FriendsLook.Display);
        _addResult.Margin = new Thickness(2, 5, 0, 0);
        _addResult.Tag = "friends-add-result";
        sp.Children.Add(_addResult);
        _addBox.Child = sp;
    }

    /// <summary>Sends the typed code and words the answer under the box. Internal for the suite.</summary>
    internal async Task<AddResult> AddByCodeAsync(string? typed = null)
    {
        var full = FriendsDrawerRules.FullCode(typed ?? _codeBox?.Text);
        if (full == null || _svc == null) return AddResult.NotFound;
        if (_addGo != null) _addGo.IsEnabled = false;
        AddResult r;
        try { r = await _svc.AddByCodeAsync(full); }
        catch { r = AddResult.TryLater; }
        bool good = FriendsDrawerRules.IsGood(r);
        if (_addResult != null)
        {
            _addResult.Text = Loc.Get(FriendsDrawerRules.AddResultKey(r));
            _addResult.Foreground = good ? FriendsLook.MintBrush : FriendsLook.GoldBrush;
        }
        if (good)
        {
            if (_addGo != null) Shockwave(_addGo, FriendsLook.Mint);
            if (_codeBox != null) _codeBox.Text = "";
            _ = SafeRefreshAsync();
        }
        else if (_addGo != null) _addGo.IsEnabled = true;
        return r;
    }

    /// <summary>The last worded add result, for the suite.</summary>
    internal string AddResultText => _addResult?.Text ?? "";

    // ---- results ----------------------------------------------------------------------

    /// <summary>Words a send in the friend's row for two seconds.</summary>
    internal void ShowResult(string friendId, SendResult r)
        => ShowTimed(friendId, Loc.Get(FriendsDrawerRules.SendResultKey(r)), FriendsDrawerRules.IsGood(r),
            TimeSpan.FromSeconds(2));

    /// <summary>Words a line in the friend's row for <paramref name="hold"/>.</summary>
    private void ShowTimed(string friendId, string text, bool good, TimeSpan hold)
    {
        _results[friendId] = (text, good);
        if (_resultTimers.TryGetValue(friendId, out var old)) old.Stop();
        var t = new DispatcherTimer { Interval = hold };
        t.Tick += (_, _) =>
        {
            t.Stop();
            _resultTimers.Remove(friendId);
            if (_results.Remove(friendId)) Render();
        };
        _resultTimers[friendId] = t;
        t.Start();
        Render();
    }

    /// <summary>The worded result showing in a row right now, or null.</summary>
    internal string? ResultTextFor(string friendId)
        => _results.TryGetValue(friendId, out var r) ? r.Text : null;

    // ---- for the suite ----------------------------------------------------------------

    /// <summary>Row ids in drawing order. Requests are prefixed "in:" and "out:".</summary>
    internal IReadOnlyList<string> RowIds => _rowOrder;

    /// <summary>Section loc keys in drawing order.</summary>
    internal IReadOnlyList<string> SectionKeys => _sectionOrder;

    internal FrameworkElement? RowFor(string id) => _rows.TryGetValue(id, out var r) ? r : null;

    internal string? OpenFriendId => _openId;

    internal string? OpenPicker => _picker;

    /// <summary>Opens or folds a friend's card. One card open at a time.</summary>
    internal void Toggle(string friendId)
    {
        if (_openId == friendId) { _openId = null; _picker = null; }
        else { _openId = friendId; _picker = null; }
        Render();
        if (_openId != null && _rows.TryGetValue(_openId, out var row)) CardIn(row);
    }

    /// <summary>Opens a picker in the open card (the suite's way to press an action).</summary>
    internal void OpenPickerFor(string friendId, string picker)
    {
        _openId = friendId;
        _picker = picker;
        Render();
    }

    // ---- who am I ---------------------------------------------------------------------

    /// <summary>The name in the header. Overridable by the suite.</summary>
    internal Func<string> MeName { get; set; } = () =>
    {
        try { return string.IsNullOrWhiteSpace(App.UserDisplayName) ? Loc.Get("friends_you") : App.UserDisplayName!.Trim(); }
        catch { return "you"; }
    };

    /// <summary>0 free, 1 Basic, 2 Prime.</summary>
    internal Func<int> MeTier { get; set; } = () =>
    {
        try { return (int)(App.Patreon?.CurrentTier ?? Models.PatreonTier.None) switch { >= 2 => 2, 1 => 1, _ => 0 }; }
        catch { return 0; }
    };
}

/// <summary>
/// The once-only presence question. The answer itself lives on the service
/// (<see cref="IFriendsService.PresenceShared"/>); this only remembers that the question was
/// asked, as a marker file beside the settings, so it never nags twice. Swappable for the suite.
/// </summary>
internal static class PresenceAsk
{
    internal static Func<bool> Asked { get; set; } = () =>
    {
        try { return File.Exists(MarkerPath); } catch { return false; }
    };

    internal static Action MarkAsked { get; set; } = () =>
    {
        try { File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("o")); } catch { }
    };

    private static string MarkerPath => Path.Combine(App.UserDataPath, "friends_presence_asked.flag");
}
