// PORTED from ConditioningControlPanel/Controls/Friends/FriendsDrawer.cs (+ .Honesty, .Blocked, .Presence):
// your head with the presence switch, the list (online, requests,
// offline, blocked), a friend's card with the menu (squelch, remove and block after a confirm,
// report), request answers, add by code and your own code. Driven only by IFriendsService.
// The send pickers (poke / invite / watch) are in FriendsDrawer.Pickers.cs.
// The feed is FriendsDrawer.Feed.cs, the sent trail and lock chip FriendsDrawer.Trail.cs; the leash section mounts through MountLeash (FriendsDrawer.Leash.cs).
// Open tables are FriendsDrawer.Tables.cs, the juice FriendsDrawer.Juice.cs, the sounds Friends/FriendsSfx.cs.
// ponytail: no per-PC block
// list (the server's list only). A word WPF throws outside the drawer
// (FriendsLanding.Tell) is a FloatingWord over the rail chip's window (OwnerWindow).
using System;
using System.Collections.Generic;
using Avalonia.Input.Platform;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Avalonia.Views.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer : Border
{
    // The palette is FriendsLook's (Friends/FriendsLook.cs, WPF's values); these names keep the drawer's code short.
    internal static readonly IBrush Raised = FriendsLook.RaisedBrush, Line = FriendsLook.LineBrush, Line2 = FriendsLook.Line2Brush,
        Text = FriendsLook.TextBrush, Muted = FriendsLook.MutedBrush, Dim = FriendsLook.DimBrush, Lilac = FriendsLook.LilacBrush,
        Pink = FriendsLook.PinkBrush, Mint = FriendsLook.MintBrush, Gold = FriendsLook.GoldBrush, Red = FriendsLook.RedBrush,
        MintInk = FriendsLook.MintInkBrush, Foot = FriendsLook.FootBrush, OfflineDot = FriendsLook.OfflineDotBrush;
    internal static readonly FontFamily Display = FriendsLook.Display, Mono = FriendsLook.Mono;
    private static IBrush Rgb(byte r, byte g, byte b) => FriendsLook.Frozen(Color.FromRgb(r, g, b));
    public const double DrawerWidth = 300, DrawerMaxHeight = 548;
    /// <summary>The page body's widest: a list wider than this reads as a table, not a list (WPF PageMaxWidth).</summary>
    public const double PageMaxWidth = 620;

    /// <summary>True when this drawer is the body of the Social > Friends PAGE rather than the rail
    /// chip's popup: it fills its column, leaves the leash to the Leash page, and never claims
    /// Escape (on a page Escape belongs to the panic key, nothing to fold). WPF feda9c907.</summary>
    internal bool AsPage { get; }

    internal static Cursor? Hand() { try { return new Cursor(StandardCursorType.Hand); } catch { return null; } }
    private readonly Func<IFriendsService?> _resolve;
    private IFriendsService? _svc;
    private bool _subscribed, _isOpen, _showBlocked;
    private string? _openId;
    private (string Id, string What)? _confirm;
    private readonly Dictionary<string, (string Text, bool Good)> _results = new();
    private readonly Border _head = new(), _ask = new(), _addBox = new(), _foot = new();
    /// <summary>The leash section's slot (WPF _leash): kept between repaints, filled once by MountLeash.</summary>
    private readonly StackPanel _leashSlot = new() { Tag = "friends-leash-slot" };
    internal Panel LeashSlot => _leashSlot;
    /// <summary>Leash lane contract: implemented in FriendsDrawer.Leash.cs; called once, from the constructor.</summary>
    partial void MountLeash(Panel slot);
    private readonly StackPanel _list = new();
    private readonly TextBox _codeBox = new();
    private readonly Button _addGo;
    private readonly TextBlock _addResult = Label("", 11.5, Muted, Display);
    public event Action? CloseRequested;
    public event Action? SettingsRequested;
    public event Action? SignInRequested;
    /// <summary>The "Invite them" link: the host opens the invites card (WPF LauncherHost.OpenPanelInvites).</summary>
    public event Action? InvitesRequested;
    private readonly WrapPanel _inviteLine;
    /// <summary>WPF RefreshInviteLine: subscribers only (an invite week holds no codes).</summary>
    internal Func<bool> OffersInviteLink { get; set; } = () => ConditioningControlPanel.Services.Invites.InviteTicketRule.OffersInviteLink(
        CoreAccount.HasPremiumAccess, ConditioningControlPanel.Services.ProviderSubscription.IsInviteWeekOnly(AccountSeed.Patreon, AccountSeed.SubscribeStar, CoreSettings.Current));
    public FriendsDrawer() : this(null) { }
    internal FriendsDrawer(IFriendsService? service) : this(service, false) { }
    internal FriendsDrawer(IFriendsService? service, bool asPage)
    {
        AsPage = asPage;
        _resolve = service != null ? () => service : () => FriendsHead.Service;
        _svc = _resolve();
        OwnerWindow = () => TopLevel.GetTopLevel(this) as Window;
        (Width, MaxHeight, CornerRadius) = (DrawerWidth, DrawerMaxHeight, new CornerRadius(16));
        Background = FriendsLook.GlassBrush;
        (BorderBrush, BorderThickness, Focusable) = (Line2, new Thickness(1), true);
        BoxShadow = FriendsLook.DrawerShadow;   // WPF DropShadowEffect, as a BoxShadow (never an Effect)
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto") };
        var (scroll, top, line) = (new ScrollViewer { Content = _list, Padding = new Thickness(6, 4, 6, 8), MinHeight = 120 }, new Thickness(0, 1, 0, 0), Line);
        (_ask.Margin, _ask.IsVisible) = (new Thickness(10, 8, 10, 0), false);
        Grid.SetRow(_ask, 1);
        (_head.Background, _head.CornerRadius, _head.BorderBrush, _head.BorderThickness, _head.Padding) = (FriendsLook.HeadBrush, new CornerRadius(15, 15, 0, 0), line, new Thickness(0, 0, 0, 1), new Thickness(12, 12, 12, 10));
        (_addBox.IsVisible, _addBox.Padding, _addBox.BorderBrush, _addBox.BorderThickness) = (false, new Thickness(10, 8, 10, 8), line, top);
        (_foot.Background, _foot.BorderBrush, _foot.BorderThickness, _foot.CornerRadius, _foot.Padding) = (Foot, line, top, new CornerRadius(0, 0, 15, 15), new Thickness(8));
        Grid.SetRow(scroll, 2);
        Grid.SetRow(_addBox, 3);
        Grid.SetRow(_foot, 4);
        root.Children.AddRange(new Control[] { _head, _ask, scroll, _addBox, _foot });
        Grid.SetRowSpan(_fx, 5);
        root.Children.Add(_fx);
        Child = root;
        // The leash, pinned above the list (WPF field _leash = new LeashDrawerSection(), re-added on every render).
        MountLeash(_leashSlot);
        // The add box (WPF BuildAddBox): CCP- and five letters from the code alphabet.
        (_codeBox.MaxLength, _codeBox.FontFamily, _codeBox.Tag) = (9, Mono, "friends-code-box");
        ToolTip.SetTip(_codeBox, Loc.Get("friends_add_hint"));
        _addGo = Pill(Loc.Get("friends_add_go"), Mint, MintInk, "friends-add-go");
        _addGo.IsEnabled = false;
        _addGo.Margin = new Thickness(6, 0, 0, 0);
        _addGo.Click += (_, _) => _ = AddByCodeAsync();
        _codeBox.TextChanged += (_, _) =>
        {
            var n = FriendsDrawerRules.NormaliseCode(_codeBox.Text);
            if (n != _codeBox.Text) { _codeBox.Text = n; _codeBox.CaretIndex = n.Length; }
            _addGo.IsEnabled = n.Length == FriendsDrawerRules.CodeLength;
        };
        _codeBox.KeyDown += (_, e) => { if (e.Key == Key.Enter && _addGo.IsEnabled) { _ = AddByCodeAsync(); e.Handled = true; } };
        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var prefix = Label(FriendsDrawerRules.CodePrefix, 14, Dim, Mono);
        prefix.Margin = new Thickness(0, 0, 4, 0);
        prefix.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_codeBox, 1);
        Grid.SetColumn(_addGo, 2);
        addRow.Children.AddRange(new Control[] { prefix, _codeBox, _addGo });
        _addResult.Margin = new Thickness(2, 5, 0, 0);
        _addResult.Tag = "friends-add-result";
        // No account yet? A subscriber has an invite code for that (WPF main e2d4e35ef): the link opens
        // the invites card. Shown only to subscribers; a button, so it is keyboard-reachable.
        var inviteLink = Pill(Loc.Get("friends_invite_link"), Brushes.Transparent, Pink, "friends-invite-link");
        inviteLink.Padding = new Thickness(0);
        inviteLink.Click += (_, _) => InvitesRequested?.Invoke();
        _inviteLine = new WrapPanel { Margin = new Thickness(2, 6, 0, 0), Tag = "friends-invite-line",
            Children = { Label(Loc.Get("friends_invite_line") + " ", 11.5, Muted, Display), inviteLink } };
        _addBox.Child = new StackPanel { Children = { addRow, _addResult, _inviteLine } };
        // Esc closes the add box, then the drawer (WPF OnKey).
        if (asPage) (Width, MaxWidth, MaxHeight, BoxShadow) = (double.NaN, PageMaxWidth, double.PositiveInfinity, default);   // a page has no shadow
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || AsPage) return;
            if (_addBox.IsVisible && _codeBox.IsFocused) _addBox.IsVisible = false;
            else if (_picker != null) { _picker = null; Render(); }
            else CloseRequested?.Invoke();
            e.Handled = true;
        };
        Render();
    }
    internal IFriendsService? Service => _svc;

    /// <summary>The name in the header (WPF App.UserDisplayName, else "you").</summary>
    internal Func<string> MeName { get; set; } = () =>
        string.IsNullOrWhiteSpace(CoreAccount.DisplayName) ? Loc.Get("friends_you") : CoreAccount.DisplayName!.Trim();
    /// <summary>Your own picture (ShareProfilePicture + Discord), or null for initials.</summary>
    internal Func<string?> MeAvatarUrl { get; set; } = () => Helpers.AvatarPhotos.OwnUrl(128);
    public void OnOpened()
    {
        _isOpen = true;
        Rebind();
        try { _svc?.SetDrawerOpen(true); } catch { }
        if (_svc?.Available == true) _ = SafeRefreshAsync();
        try { if (_svc?.Available == true) StartTables(); } catch { }
        Render();
        PlayEntrance();
        Dispatcher.UIThread.Post(() => Focus());
    }
    public void OnClosed()
    {
        try { _svc?.SetDrawerOpen(false); } catch { }
        StopTables();
        StopAmbient();
        Unsubscribe(); // a folded drawer redraws nothing (OnOpened re-subscribes)
        FeedFolded();
        (_isOpen, _openId, _confirm, _picker, _addBox.IsVisible) = (false, null, null, null, false);
    }
    private void Rebind()
    {
        var next = _resolve();
        if (!ReferenceEquals(next, _svc)) { Unsubscribe(); _svc = next; }
        if (_subscribed || _svc == null) return;
        _svc.SnapshotChanged += OnSnapshot;
        _svc.SentTrailsChanged += OnTrailsChanged;
        _subscribed = true;
    }
    public void Unsubscribe()
    {
        if (_subscribed && _svc != null) { _svc.SnapshotChanged -= OnSnapshot; _svc.SentTrailsChanged -= OnTrailsChanged; }
        _subscribed = false;
    }
    private FriendsSnapshot _last = FriendsSnapshot.Empty;
    private void OnSnapshot(FriendsSnapshot snap)
    {
        // WPF: a friend who just came online hops once after the repaint.
        var hop = FriendsDrawerRules.CameOnline(_last, snap);
        FriendsBlockList.MigrateIfDue(_svc);   // WPF: the landing tick; a server list retires this PC list
        Render();
        foreach (var id in hop) HopAvatar(id);
    }
    private async Task SafeRefreshAsync()
    {
        try { if (_svc != null) await _svc.RefreshAsync(); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] refresh failed: {E}", ex.Message); }
    }
    internal void Render()
    {
        var snap = FriendsSnapshot.Empty;
        try { if (_svc?.Available == true) snap = _svc.Snapshot ?? FriendsSnapshot.Empty; } catch { }
        _last = snap;
        RenderHead();
        RenderAsk();
        RenderList(snap);
        RenderFoot(snap);
        if (_isOpen || AsPage) StartAmbient();
    }

    /// <summary>WPF MeTier: your plan as a plate in the header (2 Prime, 1 Basic, 0 none).</summary>
    internal Func<int> MeTier { get; set; } = () =>
    {
        try { return CoreAccount.HasLabAccess ? 2 : CoreAccount.HasPremiumAccess ? 1 : 0; } catch { return 0; }
    };

    /// <summary>Test seam over FriendsLook.TierArt (kept here for the suite).</summary>
    internal static Func<int, IImage?> TierArt { get => FriendsLook.TierArt; set => FriendsLook.TierArt = value; }
    /// <summary>WPF FriendsLook.TierPlate.</summary>
    internal static Control? TierPlate(int tier, double height) => FriendsLook.TierPlate(tier, height);
    private bool Shared() { try { return _svc?.Available == true && _svc.PresenceShared; } catch { return false; } }
    private void RenderHead()
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var name = MeName();
        var avatar = Avatar(name, 40, _svc?.Available == true ? Shared() : null, MeAvatarUrl());
        avatar.Margin = new Thickness(0, 0, 10, 0);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        nameLine.Children.Add(Label(name, 16, Text, Display, FontWeight.SemiBold));
        if (TierPlate(MeTier(), 15) is { } plate) nameLine.Children.Add(plate);
        who.Children.Add(nameLine);
        if (_svc?.Available != true)
            who.Children.Add(Tagged(Label(Loc.Get("friends_signed_out"), 12, Muted), "friends-me-status"));
        else
        {
            // The status line IS the presence switch (WPF FriendsDrawer.Presence.cs).
            bool shared = Shared();
            var words = Label(Loc.Get(shared ? "friends_me_sharing" : "friends_me_hidden"), 12, shared ? Mint : Muted);
            words.Tag = "friends-me-status-text";
            var dot = new Ellipse { Width = 7, Height = 7, Fill = shared ? Mint : OfflineDot, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var pill = Pill(new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, words } }, Brushes.Transparent, Muted, "friends-me-status", Line);
            pill.HorizontalAlignment = HorizontalAlignment.Left;
            pill.Margin = new Thickness(0, 3, 0, 0);
            ToolTip.SetTip(pill, Loc.Get("friends_presence_toggle_tip"));
            pill.Click += (_, _) => TogglePresence();
            who.Children.Add(pill);
        }
        Grid.SetColumn(who, 1);
        g.Children.Add(avatar);
        g.Children.Add(who);
        _head.Child = g;
    }

    /// <summary>WPF RenderAsk: the once-only "let friends see you?" strip, while hidden and not yet asked.</summary>
    private void RenderAsk()
    {
        (_ask.Child, _ask.IsVisible) = (null, false);
        if (_svc?.Available != true || Shared() || PresenceAsk.Asked()) return;
        var q = Wrap(Label(Loc.Get("friends_presence_ask"), 12.5, Text, null, FontWeight.SemiBold));
        var sub = Wrap(Label(Loc.Get("friends_presence_ask_sub"), 11, Muted));
        sub.Margin = new Thickness(0, 2, 0, 0);
        var yes = Pill(Loc.Get("friends_presence_yes"), Mint, MintInk, "friends-presence-yes", Mint);
        yes.Click += (_, _) =>
        {
            try { if (_svc != null) _svc.PresenceShared = true; } catch { }
            PresenceAsk.MarkAsked();
            Render();
        };
        var no = Pill(Loc.Get("friends_presence_no"), Raised, Text, "friends-presence-no", Line2);
        no.Margin = new Thickness(6, 0, 0, 0);
        no.Click += (_, _) => { PresenceAsk.MarkAsked(); Render(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), Children = { yes, no } };
        Grid.SetColumn(buttons, 1);
        _ask.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0x5F, 0xFF, 0xD0)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x5F, 0xFF, 0xD0)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 6, 6, 6), Tag = "friends-presence-ask",
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { q, sub } }, buttons },
            },
        };
        _ask.IsVisible = true;
    }

    internal void TogglePresence()
    {
        if (_svc?.Available != true) return;
        try { _svc.PresenceShared = !_svc.PresenceShared; } catch (Exception ex) { Serilog.Log.Debug("[Friends] presence write failed: {E}", ex.Message); }
        PresenceAsk.MarkAsked(); // WPF FriendsPresenceSetting.Write: answering here counts as answering the ask
        Render();
    }
    private void RenderList(FriendsSnapshot snap)
    {
        StopAmbient();
        _list.Children.Clear();
        _avatars.Clear();
        _rowsInOrder.Clear();
        if (_svc?.Available != true)
        {
            var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(18, 28, 18, 28) };
            var line = EmptyLine(Loc.Get("friends_signed_out"));
            line.Margin = new Thickness(0, 0, 0, 12);
            var btn = Pill(Loc.Get("friends_sign_in"), Mint, MintInk, "friends-sign-in");
            btn.HorizontalAlignment = HorizontalAlignment.Center;
            btn.Click += (_, _) => SignInRequested?.Invoke();
            box.Children.Add(line);
            box.Children.Add(btn);
            _list.Children.Add(box);
            return;
        }
        // WPF RenderList: the leash pinned on top, then "What happened", then the people.
        if (_leashSlot.Parent is Panel old) old.Children.Remove(_leashSlot);
        if (!AsPage) _list.Children.Add(_leashSlot);   // a page leaves the leash to the Leash page
        AddFeed();
        var (online, offline) = FriendsDrawerRules.Split(snap);
        // A friend hosting an open table floats to the top (WPF FriendsDrawer.Tables.cs).
        (online, offline) = FriendsDrawerRules.HostingFirst(online, offline, f => TableFor(f) != null);
        if (_openId != null && !Contains(snap, _openId)) (_openId, _picker) = (null, null);
        if (_confirm is { } c && !Contains(snap, c.Id)) _confirm = null;
        if (online.Count > 0)
        {
            Section("friends_section_online", online.Count);
            foreach (var f in online) _list.Children.Add(FriendRow(f));
        }
        // Requests sit above the offline list: they are something to answer.
        int req = snap.Incoming.Count + snap.Outgoing.Count;
        if (req > 0)
        {
            Section("friends_section_requests", req);
            foreach (var r in snap.Incoming) _list.Children.Add(RequestRow(r, incoming: true));
            foreach (var r in snap.Outgoing) _list.Children.Add(RequestRow(r, incoming: false));
        }
        if (offline.Count > 0)
        {
            Section("friends_section_offline", offline.Count);
            foreach (var f in offline) _list.Children.Add(FriendRow(f));
        }
        if (online.Count + offline.Count + req == 0 && !_showBlocked) _list.Children.Add(AsPage ? PageEmptyBlock() : EmptyLine(Loc.Get("friends_empty")));
        if (!_showBlocked) return;
        var blocked = BlockedRows();
        Section("friends_section_blocked", blocked.Count);
        if (blocked.Count == 0)
            _list.Children.Add(Tagged(Wrap(Label(Loc.Get(snap.Blocked != null ? "friends_blocked_none" : "friends_blocked_empty"), 11.5, Muted)), "friends-blocked-empty"));
        foreach (var b in blocked) _list.Children.Add(BlockedRow(b));
    }
    private static bool Contains(FriendsSnapshot snap, string id)
    {
        foreach (var f in snap.Friends) if (f.Id == id) return true;
        return false;
    }
    private void Section(string key, int count)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 10, 8, 4) };
        var t = Label(Loc.Get(key).ToUpperInvariant(), 10.5, Dim, Mono, FontWeight.SemiBold);
        var n = Label(count.ToString(), 10.5, Dim, Mono);
        n.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(n, 1);
        g.Children.Add(t);
        g.Children.Add(n);
        _list.Children.Add(g);
    }
    private Control FriendRow(Friend f)
    {
        bool open = _openId == f.Id;
        var table = TableFor(f);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("38,*,Auto") };
        var avatar = Avatar(f.Name, 38, f.Online || table != null, f.AvatarUrl);
        if (!f.Online && table == null) avatar.Opacity = 0.55;
        avatar.RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);
        avatar.RenderTransform = new TranslateTransform();
        _avatars[f.Id] = avatar;
        var mid = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        nameLine.Children.Add(Label(f.Name, 14, f.Online ? Text : Muted, Display, FontWeight.Medium));
        if (TierPlate(f.Tier, 12) is { } plate) nameLine.Children.Add(plate);
        if (f.Squelched)
        {
            var sq = Tagged(Label(Loc.Get("friends_squelched_tag"), 9, Dim, Mono), "friends-squelched");
            sq.Margin = new Thickness(6, 1, 0, 0);
            nameLine.Children.Add(sq);
        }
        mid.Children.Add(nameLine);
        mid.Children.Add(table != null ? HostingLine() : ActivityLine(f));
        // What you last sent them and how far it got (sent, arrived, seen, answered).
        if (TrailLine(f) is { } trail) mid.Children.Add(trail);
        Grid.SetColumn(mid, 1);
        top.Children.Add(avatar);
        top.Children.Add(mid);
        if (LockChip(f) is { } lockChip) { Grid.SetColumn(lockChip, 2); top.Children.Add(lockChip); }
        Button? join = null;
        if (table != null)
        {
            top.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            join = TableJoinButton(f, table);
            Grid.SetColumn(join, 3);
            top.Children.Add(join);
        }
        var outer = new StackPanel { Children = { top } };
        if (open) outer.Children.Add(Card(f));
        if (_results.TryGetValue(f.Id, out var res))
        {
            var line = Tagged(Label(res.Text, 11.5, res.Good ? Mint : Gold, Display), "friends-result");
            line.Margin = new Thickness(48, 4, 0, 0);
            outer.Children.Add(line);
        }
        var row = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 1),
            Background = open ? Raised : Brushes.Transparent, Cursor = Hand(),
            Tag = "friends-row:" + f.Id, Child = outer, ContextMenu = Menu(f), ClipToBounds = true,
        };
        if (table != null && !open) DressHostingRow(row);
        AttachSheen(row, open);
        _rowsInOrder.Add(row);
        // A click on the row (not inside the open card) opens or folds its card.
        top.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) { Toggle(f.Id); e.Handled = true; } };
        return row;
    }
    private TextBlock ActivityLine(Friend f)
    {
        var t = new TextBlock { FontSize = 11.5, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "friends-activity" };
        if (f.Online)
        {
            t.Inlines!.Add(new global::Avalonia.Controls.Documents.Run("● ") { Foreground = Mint });
            t.Inlines.Add(new global::Avalonia.Controls.Documents.Run(Loc.Get(FriendsDrawerRules.ActivityKey(f.Presence.Activity))));
        }
        else
        {
            var (key, arg) = FriendsDrawerRules.SeenKey(f.Presence.LastSeen, DateTimeOffset.UtcNow);
            t.Text = arg is int n ? Loc.GetF(key, n) : Loc.Get(key);
        }
        return t;
    }
    internal void OpenOn(string friendId) { if (_openId != friendId) Toggle(friendId); }   // WPF OpenOn: a notice or an Inbox row asks for this friend's card
    internal void Toggle(string friendId)
    {
        _openId = _openId == friendId ? null : friendId;
        (_confirm, _picker) = (null, null);
        Render();
        // WPF CardIn: the card that just opened grows in under its row.
        if (_openId != null) foreach (var r in _rowsInOrder) if (r.Tag as string == "friends-row:" + _openId) CardIn(r);
    }

    /// <summary>The open card: the menu one press away, or the Remove / Block question.</summary>
    private Control Card(Friend f)
    {
        var card = new StackPanel { Margin = new Thickness(0, 8, 0, 2), Tag = "friends-card" };
        if (_confirm is { } c && c.Id == f.Id)
        {
            bool block = c.What == "block";
            var keep = Pill(Loc.Get("friends_confirm_keep"), Raised, Text, "friends-confirm-keep");
            keep.Click += (_, _) => { _confirm = null; Render(); };
            var yes = Pill(Loc.Get(block ? "friends_menu_block" : "friends_confirm_yes_remove"), Red, Text, "friends-confirm-yes");
            yes.Margin = new Thickness(6, 0, 0, 0);
            yes.Click += async (_, _) => await ConfirmAsync(f.Id, f.Name, c.What);
            card.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0x5F, 0x7A)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x5F, 0x7A)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 7, 7), Tag = "friends-confirm:" + c.What,
                Child = new StackPanel
                {
                    Children =
                    {
                        Wrap(Label(Loc.GetF(block ? "friends_confirm_block" : "friends_confirm_remove", f.Name), 12.5, Text, null, FontWeight.SemiBold)),
                        Wrap(Label(Loc.Get(block ? "friends_confirm_block_sub" : "friends_confirm_remove_sub"), 11, Muted)),
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), Children = { keep, yes } },
                    },
                },
            });
            return card;
        }
        card.Children.Add(CardActions(f));
        if (LeashSection?.OfferChipFor(f) is { } leashChip) card.Children.Add(leashChip);   // WPF: under the actions
        var more = Pill(Loc.Get("friends_action_more"), Brushes.Transparent, Muted, "friends-action:more", Line2);
        more.HorizontalAlignment = HorizontalAlignment.Stretch;
        more.HorizontalContentAlignment = HorizontalAlignment.Center;
        more.Click += (_, _) =>
        {
            Friends.FriendsSfx.Click();
            // WPF: PlacementTarget = more, Placement = Bottom (under the button, not over the list).
            var menu = Menu(f);
            menu.Placement = PlacementMode.Bottom;
            menu.Open(more);
        };
        card.Children.Add(more);
        if (_picker != null)
        {
            var picker = Picker(f);
            picker.Margin = new Thickness(0, 8, 0, 0);
            card.Children.Add(picker);
        }
        return card;
    }

    /// <summary>Right-click and "more" (WPF BuildMenu): squelch, remove, block, report by reason.</summary>
    private ContextMenu Menu(Friend f)
    {
        var items = new List<Control>();
        foreach (var id in FriendsDrawerRules.MenuItems(f.Squelched))
        {
            if (id == null) { items.Add(new Separator { Background = Line, Tag = "friends-menu-separator" }); continue; }
            var mi = MenuItem(id);
            if (id == "report")
            {
                var subs = new List<MenuItem>();
                foreach (var reason in ReportReason.All)
                {
                    var sub = MenuItem("report_" + reason);
                    sub.Click += async (_, _) => await ReportAsync(f.Id, reason);
                    subs.Add(sub);
                }
                mi.ItemsSource = subs;
            }
            else mi.Click += async (_, _) => await RunMenuAsync(f, id);
            items.Add(mi);
        }
        // WPF BuildMenu's look: dark glass, Line2 edge, Fredoka 13.5. Right-click opens at the pointer.
        return new ContextMenu
        {
            ItemsSource = items, Tag = "friends-menu:" + f.Id, Placement = PlacementMode.Pointer,
            Background = MenuGlass, BorderBrush = Line2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Foreground = Text, FontFamily = Display, FontSize = 13.5, Padding = new Thickness(4),
        };
    }
    private static readonly IBrush MenuGlass = FriendsLook.MenuBrush;
    /// <summary>One menu line. Every line names its brush: an explicit null Foreground in Avalonia is
    /// "no brush", which drew Squelch and Remove as empty lines (owner report, 2026-10-09).</summary>
    private static MenuItem MenuItem(string id) => new()
    {
        Header = new TextBlock { Text = Loc.Get("friends_menu_" + id), Foreground = id is "block" or "report" ? Red : Text, FontFamily = Display, FontSize = 13.5 },
        Tag = "friends-menu-item:" + id, Foreground = id is "block" or "report" ? Red : Text, Background = Brushes.Transparent,
    };

    internal async Task RunMenuAsync(Friend f, string what)
    {
        if (_svc == null) return;
        if (what is "remove" or "block") { _confirm = (f.Id, what); _openId = f.Id; Render(); return; }
        ActResult r = ActResult.TryLater;
        try { r = await _svc.SetSquelchAsync(f.Id, what == "squelch"); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] menu {What} failed: {E}", what, ex.Message); }
        if (r == ActResult.Done) ShowTimed(f.Id, Loc.Get(what == "squelch" ? "friends_squelch_done" : "friends_unsquelch_done"), true);
        else ShowTimed(f.Id, Loc.Get(FriendsDrawerRules.ActResultKey(r)), false);
        await SafeRefreshAsync();
    }
    internal async Task<ActResult> ReportAsync(string friendId, string reason, string? rowId = null)
    {
        if (_svc == null) return ActResult.TryLater;
        Friends.FriendsSfx.Click();
        ActResult r;
        try { r = await _svc.ReportAsync(friendId, reason); } catch { r = ActResult.TryLater; }
        if (r == ActResult.Done) ShowTimed(rowId ?? friendId, Loc.Get("friends_report_done"), true);
        else ShowActResult(rowId ?? friendId, r);
        return r;
    }

    internal async Task<ActResult> ConfirmAsync(string id, string name, string what)
    {
        _confirm = null;
        if (_svc == null) return ActResult.TryLater;
        ActResult r;
        try { r = what == "block" ? await _svc.BlockAsync(id) : await _svc.RemoveAsync(id); }
        catch { r = ActResult.TryLater; }
        if (r == ActResult.Done)
        {
            Friends.FriendsSfx.Dismiss();
            if (what == "block") OnBlocked(id, name);
            if (_openId == id) (_openId, _picker) = (null, null);
            // The row is gone after the refresh, so the word goes where the player is looking.
            TellOutside(Loc.GetF(what == "block" ? "friends_blocked_done" : "friends_removed_done", name), good: true, always: true);
        }
        else ShowActResult(id, r);
        await SafeRefreshAsync();
        Render();
        return r;
    }
    private Control RequestRow(FriendRequest r, bool incoming)
    {
        var rowId = (incoming ? "in:" : "out:") + r.Id;
        var mid = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(Label(r.Name, 14, Text, Display, FontWeight.Medium));
        string sub = !incoming ? Loc.Get("friends_request_waiting")
            : !string.IsNullOrEmpty(r.Via) ? Loc.GetF("friends_request_via", r.Via!)
            : Loc.Get("friends_request_new");
        var (agoKey, agoArg) = FriendsDrawerRules.RequestAgo(r.At, DateTimeOffset.UtcNow);
        if (agoKey != null) sub = (agoArg is int n ? Loc.GetF(agoKey, n) : Loc.Get(agoKey)) + " \u00B7 " + sub;
        mid.Children.Add(Tagged(Label(sub, 11.5, Muted), "friends-request-sub"));
        if (_results.TryGetValue(rowId, out var res)) mid.Children.Add(Tagged(Label(res.Text, 11.5, res.Good ? Mint : Gold, Display), "friends-result"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (incoming)
        {
            // Drawn in an open drawer: the one who asked may hear it was seen (once per request).
            if (_isOpen) FriendsSeen.Shared.RequestSeen(_svc, r);
            var accept = Pill(Loc.Get("friends_request_accept"), Mint, MintInk, "friends-accept");
            accept.Click += async (_, _) => await AnswerRequestAsync(r, "accept", accept);
            var decline = Pill(Loc.Get("friends_request_decline"), Raised, Text, "friends-decline");
            decline.Margin = new Thickness(6, 0, 0, 0);
            decline.Click += async (_, _) => await AnswerRequestAsync(r, "decline");
            buttons.Children.Add(accept);
            buttons.Children.Add(decline);
        }
        else
        {
            var cancel = Pill(Loc.Get("friends_request_cancel"), Raised, Muted, "friends-cancel");
            cancel.Click += async (_, _) => await AnswerRequestAsync(r, "cancel");
            buttons.Children.Add(cancel);
        }
        Grid.SetColumn(mid, 1);
        Grid.SetColumn(buttons, 2);
        return new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 1),
            Tag = (incoming ? "friends-request-in:" : "friends-request-out:") + r.Id,
            ContextMenu = incoming ? RequestMenu(r) : null,
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("38,*,Auto"), Children = { Avatar(r.Name, 38, null, r.AvatarUrl), mid, buttons } },
        };
    }

    internal async Task<ActResult> AnswerRequestAsync(FriendRequest r, string what, Control? from = null)
    {
        if (_svc == null) return ActResult.TryLater;
        Friends.FriendsSfx.Click();
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
            if (what == "accept") { if (from != null) Shockwave(from, MintC); Friends.FriendsSfx.Accepted(); TellOutside(Loc.Get("friends_add_accepted"), good: true); }
            else Friends.FriendsSfx.Dismiss();
        }
        else ShowActResult((what == "cancel" ? "out:" : "in:") + r.Id, res);
        await SafeRefreshAsync();
        return res;
    }
    private Control BlockedRow(BlockedEntry b)
    {
        var avatar = Avatar(b.Name, 30, null);
        avatar.Opacity = 0.5;
        var mid = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(Label(b.Name, 13, Muted, Display, FontWeight.Medium));
        if (_results.TryGetValue("blocked:" + b.Id, out var res)) mid.Children.Add(Label(res.Text, 11, res.Good ? Mint : Gold, Display));
        var unblock = Pill(Loc.Get("friends_unblock"), Raised, Text, "friends-unblock");
        unblock.Click += async (_, _) => await UnblockAsync(b);
        Grid.SetColumn(mid, 1);
        Grid.SetColumn(unblock, 2);
        return new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 1), Tag = "friends-blocked:" + b.Id,
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,Auto"), Children = { avatar, mid, unblock } },
        };
    }
    internal Task<ActResult> UnblockAsync(BlockedFriend b) => UnblockAsync(new BlockedEntry(FriendsBlockList.Account() ?? "", b.Id, b.Name, DateTimeOffset.MinValue));
    /// <summary>Unblock, and forget the block on this PC only once the server agreed.</summary>
    internal async Task<ActResult> UnblockAsync(BlockedEntry b)
    {
        if (_svc == null) return ActResult.TryLater;
        Friends.FriendsSfx.Click();
        ActResult r;
        try { r = await _svc.UnblockAsync(b.Id); } catch { r = ActResult.TryLater; }
        if (r == ActResult.Done)
        {
            try { FriendsBlockList.Shared.Remove(b.Account, b.Id); } catch { }
            Friends.FriendsSfx.Accepted();
            TellOutside(Loc.GetF("friends_unblocked_done", b.Name), good: true, always: true);
            await SafeRefreshAsync();
            Render();
        }
        else ShowActResult("blocked:" + b.Id, r);
        return r;
    }
    internal void ToggleBlocked() { _showBlocked = !_showBlocked; Render(); }
    private void RenderFoot(FriendsSnapshot snap)
    {
        // WPF RenderFoot: Settings (gear + words), the bell, the Blocked glyph; Add friend on the right.
        var settings = FootButton("\uE713", Loc.Get("friends_settings"), "friends-settings");
        settings.Click += (_, _) => { SettingsRequested?.Invoke(); CloseRequested?.Invoke(); };
        // The bell: corner notices for pokes, knocks and requests. Off keeps the Inbox rows and the cue.
        var bellGlyph = Glyph("\uE7ED", NoticesOn() ? Lilac : Dim);
        var bell = Pill(bellGlyph, Brushes.Transparent, Muted, "friends-notices");
        bell.Padding = new Thickness(8, 6, 8, 6);
        ToolTip.SetTip(bell, Loc.Get(NoticesOn() ? "friends_notices_on" : "friends_notices_off"));
        bell.Click += (_, _) =>
        {
            ToggleNotices();
            bellGlyph.Foreground = NoticesOn() ? Lilac : Dim;
            ToolTip.SetTip(bell, Loc.Get(NoticesOn() ? "friends_notices_on" : "friends_notices_off"));
        };
        var blocked = Pill(Glyph("\uE8F8", _showBlocked ? Lilac : Dim), Brushes.Transparent, Muted, "friends-blocked-toggle");
        blocked.Padding = new Thickness(8, 6, 8, 6);
        ToolTip.SetTip(blocked, Loc.Get("friends_blocked_title"));
        blocked.IsEnabled = _svc?.Available == true;
        blocked.Click += (_, _) => ToggleBlocked();
        var add = FootButton("\uE8FA", Loc.Get("friends_add_title"), "friends-add-open");
        add.HorizontalAlignment = HorizontalAlignment.Right;
        add.IsEnabled = _svc?.Available == true;
        add.Click += (_, _) => { Friends.FriendsSfx.Click(); ToggleAddBox(!_addBox.IsVisible); };
        Grid.SetColumn(add, 1);
        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { settings, bell, blocked } }, add },
        };
        // "Add friend" keeps its words while they fit beside the left buttons; in a longer language it is
        // the glyph alone with the words as its tooltip (WPF RenderFoot).
        var room = DrawerWidth - 2 - _foot.Padding.Left - _foot.Padding.Right;
        var any = new Size(double.PositiveInfinity, double.PositiveInfinity);
        buttons.Children[0].Measure(any);
        add.Measure(any);
        if (buttons.Children[0].DesiredSize.Width + add.DesiredSize.Width > room && add.Content is StackPanel parts && parts.Children.Count == 2)
        {
            parts.Children[0].Margin = new Thickness(0);
            parts.Children[1].IsVisible = false;
            ToolTip.SetTip(add, Loc.Get("friends_add_title"));
        }
        var foot = new StackPanel();
        if (!string.IsNullOrEmpty(snap.MyCode))
        {
            // Your code on a row of its own: it is what a friend types to add you.
            var code = snap.MyCode;
            var copied = Label(Loc.Get("friends_my_code"), 9, Dim, Mono);
            copied.VerticalAlignment = VerticalAlignment.Center;
            var copy = Pill(Glyph("\ue8c8", Muted, 11), Brushes.Transparent, Muted, "friends-copy");
            (copy.Padding, copy.Margin) = (new Thickness(5, 3, 5, 3), new Thickness(4, 0, 0, 0));
            ToolTip.SetTip(copy, Loc.Get("friends_copy"));
            copy.Click += async (_, _) =>
            {
                try { await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(code) ?? Task.CompletedTask); } catch { return; }
                copied.Text = Loc.Get("friends_copied");
                copied.Foreground = Mint;
                Friends.FriendsSfx.Click();
                Pop(copy, LilacC);
                DispatcherTimer.RunOnce(() => { copied.Text = Loc.Get("friends_my_code"); copied.Foreground = Dim; }, TimeSpan.FromSeconds(2));
            };
            var mine = new StackPanel { Orientation = Orientation.Horizontal, Children = { Tagged(Label(code, 12, Lilac, Mono, FontWeight.SemiBold), "friends-my-code"), copy } };
            mine.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(mine, 1);
            foot.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 0, 2, 4), Children = { copied, mine } });
        }
        foot.Children.Add(buttons);
        _foot.Child = foot;
    }

    /// <summary>The bell's state (AppSettings.FriendNotificationsEnabled). Swappable for the suite.</summary>
    internal static Func<bool> NoticesOn { get; set; } = () => CoreSettings.Current?.FriendNotificationsEnabled != false;
    internal static Action ToggleNotices { get; set; } = () =>
    {
        var s = CoreSettings.Current;
        if (s == null) return;
        s.FriendNotificationsEnabled = !s.FriendNotificationsEnabled;
        try { CoreSettings.Save(); } catch { }
    };
    internal const string GlyphFont = "Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI";
    private static TextBlock Glyph(string glyph, IBrush fg, double size = 13) => new()
    {
        Text = glyph, FontFamily = new FontFamily(GlyphFont), FontSize = size, Foreground = fg, VerticalAlignment = VerticalAlignment.Center,
    };
    /// <summary>WPF FootButton: an MDL2 glyph and the words, flat until hovered.</summary>
    private static Button FootButton(string glyph, string text, string tag)
    {
        var g = Glyph(glyph, Muted);
        g.Margin = new Thickness(0, 0, 6, 0);
        var b = Pill(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { g, new TextBlock { Text = text, FontFamily = Display, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center } },
        }, Brushes.Transparent, Muted, tag);
        b.Padding = new Thickness(8, 6, 8, 6);
        return b;
    }

    internal async Task<AddResult> AddByCodeAsync(string? typed = null)
    {
        var full = FriendsDrawerRules.FullCode(typed ?? _codeBox.Text);
        if (full == null || _svc == null) return AddResult.NotFound;
        _addGo.IsEnabled = false;
        AddResult r;
        try { r = await _svc.AddByCodeAsync(full); } catch { r = AddResult.TryLater; }
        bool good = FriendsDrawerRules.IsGood(r);
        _addResult.Text = Loc.Get(FriendsDrawerRules.AddResultKey(r));
        _addResult.Foreground = good ? Mint : Gold;
        if (good) { Shockwave(_addGo, MintC); Friends.FriendsSfx.Accepted(); } else Friends.FriendsSfx.Denied();
        if (good) _codeBox.Text = "";
        else _addGo.IsEnabled = FriendsDrawerRules.NormaliseCode(_codeBox.Text).Length == FriendsDrawerRules.CodeLength;
        await SafeRefreshAsync();
        return r;
    }


    private void ShowTimed(string rowId, string text, bool good)
    {
        _results[rowId] = (text, good);
        Render();
        DispatcherTimer.RunOnce(() => { if (_results.TryGetValue(rowId, out var cur) && cur.Text == text) { _results.Remove(rowId); Render(); } },
            TimeSpan.FromSeconds(FriendsDrawerRules.ResultHoldSeconds));
    }
    /// <summary>The window a word outside the drawer flies over. The drawer lives in a popup (not a Window) and is
    /// detached once folded, so its host (the rail chip) sets this to its own window.</summary>
    internal Func<Window?> OwnerWindow { get; set; }
    /// <summary>WPF TellOutside(always) -> FriendsLanding.Say: a floating word over the window.</summary>
    private void Say(string text, bool good) =>
        Overlays.FloatingWord.Throw(OwnerWindow(), text, pink: !good, small: true);
    internal static TextBlock Label(string text, double size, IBrush fg, FontFamily? font = null, FontWeight weight = FontWeight.Normal) =>
        FriendsLook.Label(text, size, fg, font, weight);
    private static TextBlock Wrap(TextBlock t) { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; return t; }
    private static T Tagged<T>(T c, string tag) where T : Control { c.Tag = tag; return c; }
    /// <summary>WPF ToggleAddBox: the add-by-code box opens (fresh result, invite line, focus) or folds.</summary>
    internal void ToggleAddBox(bool show)
    {
        _addBox.IsVisible = show;
        if (!show) return;
        _addResult.Text = "";
        _inviteLine.IsVisible = OffersInviteLink();
        _codeBox.Focus();
        if (Amount > 0) StaggerIn(_addBox, TimeSpan.Zero);   // WPF MotionFx.StaggerIn(_addBox)
    }

    /// <summary>True while the add-by-code box is open.</summary>
    internal bool AddBoxOpen => _addBox.IsVisible;

    /// <summary>The page's empty state: one line and one button ("No friends yet. Add one").</summary>
    private Control PageEmptyBlock()
    {
        var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(18, 28, 18, 28), Tag = "friends-page-empty" };
        var line = EmptyLine(Loc.Get("social_friends_empty"));
        line.Margin = new Thickness(0, 0, 0, 12);
        box.Children.Add(line);
        var btn = Pill(Loc.Get("social_friends_empty_add"), Mint, MintInk, "friends-page-empty-add", Mint);
        btn.Padding = new Thickness(16, 6, 16, 6);
        if (btn.Content is TextBlock tb) tb.FontSize = 13;
        btn.HorizontalAlignment = HorizontalAlignment.Center;
        btn.Click += (_, _) => ToggleAddBox(true);
        box.Children.Add(btn);
        return box;
    }

    private static TextBlock EmptyLine(string text)
    {
        var t = Wrap(Label(text, 12.5, Muted));
        t.TextAlignment = TextAlignment.Center;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(18, 28, 18, 28);
        t.Tag = "friends-empty";
        return t;
    }

    /// <summary>WPF FriendsLook.Pill (hover wash and lift, press squish, 40% disabled).</summary>
    internal static Button Pill(object content, IBrush bg, IBrush fg, string tag, IBrush? border = null) => FriendsLook.Pill(content, bg, fg, tag, border);

    /// <summary>WPF FriendsLook.Avatar: the name's gradient disc, a picture once it loads, the presence dot.</summary>
    internal static Control Avatar(string name, double size, bool? dot, string? url = null) => FriendsLook.Avatar(name, size, dot, url);
}

/// <summary>WPF PresenceAsk: the once-only presence question's marker, beside the settings. The ask
/// strip itself is not on this head yet; the switch still marks it, so it never nags once it is.</summary>
internal static class PresenceAsk
{
    internal static Func<bool> Asked { get; set; } = () => { try { return System.IO.File.Exists(MarkerPath); } catch { return false; } };
    internal static Action MarkAsked { get; set; } = () => { try { System.IO.File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("o")); } catch { } };
    private static string MarkerPath => System.IO.Path.Combine(CorePaths.UserData, "friends_presence_asked.flag");
}
