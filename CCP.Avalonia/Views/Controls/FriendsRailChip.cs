// PORTED from ConditioningControlPanel/Controls/Friends/FriendsRailChip.cs: your face and name at the
// foot of the rail with a mint pill for friends online; a click opens the drawer upward over the rail.
// The pink badge counts unread "What happened" lines (FriendsFeedHost.Feed).
// The tier plate follows the name, the face lifts on hover, both counts bump when they rise, and an
// open drawer holds the rail (a no-op on the always-labelled 7.1.5 rail, kept for parity) and folds
// when the host window moves, changes state or loses the player; Popup light-dismiss is the click-outside.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Friends.Feed;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed class FriendsRailChip : Grid
{
    private readonly Border _pill = new(), _face = new() { Width = 48, Height = 48, CornerRadius = new CornerRadius(14) };
    private readonly TextBlock _pillText = new(), _name;
    private readonly Grid _faceGrid = new();
    private readonly Popup _popup;
    private IFriendsService? _svc;
    private readonly Border _badge = new();
    private readonly TextBlock _badgeText = new();
    private readonly Func<FriendsFeed?> _resolveFeed;
    private FriendsFeed? _feed;
    public FriendsRailChip() : this(null) { }
    internal FriendsRailChip(IFriendsService? service, FriendsFeed? feed = null)
    {
        _resolveFeed = feed != null ? () => feed : () => Friends.FriendsFeedHost.Feed;
        // Unread feed lines: pink, bottom right, under the mint online count (WPF FriendsRailChip).
        (_badgeText.FontFamily, _badgeText.FontWeight, _badgeText.FontSize, _badgeText.HorizontalAlignment) = (FriendsDrawer.Display, FontWeight.SemiBold, 11, HorizontalAlignment.Center);
        _badgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x06, 0x1A));
        (_badge.Child, _badge.Background, _badge.CornerRadius, _badge.Padding, _badge.MinWidth) = (_badgeText, FriendsDrawer.Pink, new CornerRadius(999), new Thickness(5, 0), 18);
        (_badge.BorderThickness, _badge.BorderBrush) = (new Thickness(2), new SolidColorBrush(Color.FromRgb(0x0E, 0x09, 0x19)));
        (_badge.HorizontalAlignment, _badge.VerticalAlignment, _badge.Margin, _badge.IsVisible, _badge.Tag) = (HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -4, -2), false, "friends-chip-unread");
        Drawer = new FriendsDrawer(service) { Margin = new Thickness(ShadowRoom, ShadowRoom, ShadowRoom, ShadowRoom + 10) };
        (Height, Margin, Background, Cursor) = (48, new Thickness(0, 2, 0, 4), Brushes.Transparent, FriendsDrawer.Hand());
        ColumnDefinitions = new ColumnDefinitions("56,*");
        (_pillText.FontFamily, _pillText.FontWeight, _pillText.FontSize, _pillText.Foreground) = (FriendsDrawer.Display, FontWeight.SemiBold, 11, FriendsDrawer.MintInk);
        (_pill.Child, _pill.Background, _pill.CornerRadius, _pill.Padding, _pill.IsVisible, _pill.Tag) = (_pillText, FriendsDrawer.Mint, new CornerRadius(999), new Thickness(5, 0), false, "friends-chip-online");
        (_pill.HorizontalAlignment, _pill.VerticalAlignment, _pill.Margin) = (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -2, -4, 0));
        _face.HorizontalAlignment = HorizontalAlignment.Center;
        _face.Child = _faceGrid;
        RefreshFace();
        // The name column is 0 px wide while the rail is shut, so nothing in it may wrap.
        _name = FriendsDrawer.Label(Drawer.MeName(), 14, FriendsDrawer.Text, FriendsDrawer.Display, FontWeight.SemiBold);
        _name.Margin = new Thickness(0, 0, 10, 0);
        _nameLine.Children.Add(_name);
        Grid.SetColumn(_nameLine, 1);
        Children.AddRange(new Control[] { _face, _nameLine });
        RefreshPlate();
        // WPF Hover: the face wears the raised wash and lifts 2% (MotionFx.HoverLift, 150 ms).
        PointerEntered += (_, _) => { Hover(true); ShowNamePill(true); };
        // A tooltip or popup opening under the pointer reports an exit while the pointer is still on the
        // chip, which closed the pill after a second: ask again a moment later before closing.
        PointerExited += (_, _) =>
        {
            Hover(false);
            global::Avalonia.Threading.DispatcherTimer.RunOnce(() => { if (!IsPointerOver) ShowNamePill(false); }, TimeSpan.FromMilliseconds(140));
        };
        // Owner, 2026-10-09: the rail is too narrow for a name, which was cut mid-word. The name line
        // shows only when it fits whole; otherwise a hover shows it in a pill beside the chip.
        (_namePillText.FontFamily, _namePillText.FontWeight, _namePillText.FontSize, _namePillText.Foreground) = (FriendsDrawer.Display, FontWeight.SemiBold, 17, FriendsDrawer.Text);
        _namePillLine.Children.Add(_namePillText);
        (_namePillSub.FontFamily, _namePillSub.FontSize, _namePillSub.Foreground, _namePillSub.Margin) = (FriendsDrawer.Display, 12, FriendsDrawer.Dim, new Thickness(0, 2, 0, 0));
        _namePill = new Popup
        {
            Placement = PlacementMode.Right, PlacementTarget = this, HorizontalOffset = 6, IsLightDismissEnabled = false, IsHitTestVisible = false, Focusable = false,
            Child = new Border
            {
                Tag = "friends-chip-name", Child = new StackPanel { Children = { _namePillLine, _namePillSub } }, Background = FriendsDrawer.Raised, CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16, 9), BorderThickness = new Thickness(1), BorderBrush = FriendsDrawer.Mint, IsHitTestVisible = false,
            },
        };
        Children.Add(_namePill);
        SizeChanged += (_, _) => FitName();
        ToolTip.SetTip(this, Loc.Get("friends_chip_tooltip"));
        _popup = new Popup
        {
            Child = Drawer, Placement = PlacementMode.AnchorAndGravity, PlacementAnchor = global::Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft, PlacementGravity = global::Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.TopRight, PlacementTarget = this, HorizontalOffset = 4 - ShadowRoom, VerticalOffset = -6 + ShadowRoom + 10,
            IsLightDismissEnabled = true,
        };
        Children.Add(_popup);
        _popup.Opened += (_, _) => { ShowNamePill(false); OnPopupOpened(); };
        _popup.Closed += (_, _) => OnPopupClosed();
        Drawer.OwnerWindow = () => TopLevel.GetTopLevel(this) as Window;
        // WPF OnDrawerMouseDown: a popup never activates its owner on a click.
        Drawer.AddHandler(PointerPressedEvent, OnDrawerPressed, global::Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        Drawer.CloseRequested += () => _popup.IsOpen = false;
        Drawer.SettingsRequested += () => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("appsettings");
        Drawer.SignInRequested += () => { _popup.IsOpen = false; _ = (TopLevel.GetTopLevel(this) as MainShellWindow)?.OpenUnifiedLoginDialog(); };
        Drawer.InvitesRequested += () => { _popup.IsOpen = false; (TopLevel.GetTopLevel(this) as MainShellWindow)?.OpenInvitesCard(); };
        // Avalonia routes the popup's input through the Popup to this chip; a click inside the drawer is not a chip click.
        PointerReleased += (_, e) =>
        {
            if (e.Source is Visual s && (ReferenceEquals(s, Drawer) || Drawer.IsVisualAncestorOf(s))) return;
            if (e.InitialPressMouseButton == MouseButton.Left) { Toggle(); e.Handled = true; }
        };
        // The feed is built after the shell at startup: follow it while on screen (a static event must not hold a dead chip).
        Action feedBuilt = () => global::Avalonia.Threading.Dispatcher.UIThread.Post(RebindFeed);
        AttachedToVisualTree += (_, _) => { Friends.FriendsFeedHost.FeedChanged += feedBuilt; LocalizationManager.Instance.LanguageChanged += OnLanguage; Rebind(); };
        DetachedFromVisualTree += (_, _) => { Friends.FriendsFeedHost.FeedChanged -= feedBuilt; LocalizationManager.Instance.LanguageChanged -= OnLanguage; _popup.IsOpen = false; _namePill.IsOpen = false; Unwire(); UnwireFeed(); Drawer.Unsubscribe(); };
        Rebind();
    }
    internal FriendsDrawer Drawer { get; }

    private readonly Popup _namePill;
    private readonly TextBlock _namePillText = new(), _namePillSub = new();
    private readonly StackPanel _namePillLine = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 6 };
    internal Popup NamePill => _namePill;
    /// <summary>True while the rail has room for the whole name beside the face.</summary>
    internal bool NameFits { get; private set; }

    /// <summary>The name line shows whole or not at all (never cut mid-word).</summary>
    private void FitName()
    {
        _nameLine.Measure(Size.Infinity);
        NameFits = Bounds.Width - 56 >= _nameLine.DesiredSize.Width && Bounds.Width > 56;
        _nameLine.Opacity = NameFits ? 1 : 0;
        if (NameFits) ShowNamePill(false);
    }

    private void ShowNamePill(bool on)
    {
        if (!on || NameFits || _popup.IsOpen) { _namePill.IsOpen = false; ToolTip.SetServiceEnabled(this, NameFits); return; }
        _namePillText.Text = Drawer.MeName();
        while (_namePillLine.Children.Count > 1) _namePillLine.Children.RemoveAt(1);
        if (FriendsDrawer.TierPlate(Drawer.MeTier(), 20) is { } plate) _namePillLine.Children.Add(plate);
        // The pill says what the tooltip said (what the chip is, how many new lines), so the two never stack.
        _namePillSub.Text = _lastUnread > 0 ? Loc.Get("friends_chip_tooltip") + " · " + Loc.GetF("friends_feed_new", _lastUnread) : Loc.Get("friends_chip_tooltip");
        ToolTip.SetServiceEnabled(this, false);
        _namePill.IsOpen = true;
    }
    /// <summary>Room around the drawer inside its popup so the BoxShadow (blur 30, 10 down) is not cut off;
    /// the popup offsets take it back, so the drawer sits where it did.</summary>
    internal const double ShadowRoom = 24;

    /// <summary>Your face and name again: sign-in, sign-out and a Discord link all change them
    /// (the shell calls this beside RefreshProfileBubble). The picture follows ShareProfilePicture.</summary>
    internal void RefreshFace()
    {
        var name = Drawer.MeName();
        _faceGrid.Children.Clear();
        _faceGrid.Children.Add(FriendsDrawer.Avatar(name, 40, null, Drawer.MeAvatarUrl()));
        _faceGrid.Children.Add(_pill);
        _faceGrid.Children.Add(_badge);
        if (_name != null) _name.Text = name;
        RefreshPlate();
    }

    private readonly StackPanel _nameLine = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>WPF Repaint: your tier plate (13 px) after the name.</summary>
    private void RefreshPlate()
    {
        if (_name == null) return;
        while (_nameLine.Children.Count > 1) _nameLine.Children.RemoveAt(1);
        if (FriendsDrawer.TierPlate(Drawer.MeTier(), 13) is { } plate) _nameLine.Children.Add(plate);
    }

    private void Hover(bool on)
    {
        _face.Background = on || _popup.IsOpen ? FriendsDrawer.Raised : Brushes.Transparent;
        if (_face.RenderTransform is not ScaleTransform s) { _face.RenderTransformOrigin = RelativePoint.Center; _face.RenderTransform = s = new ScaleTransform(1, 1); }
        double to = on ? 1.02 : 1.0;
        if (FriendsDrawer.Amount <= 0) { s.ScaleX = s.ScaleY = to; return; }
        global::ConditioningControlPanel.Avalonia.Helpers.TransformTween.Run(s, TimeSpan.FromMilliseconds(150), new (double, AvaloniaProperty, double)[]
        {
            (0, ScaleTransform.ScaleXProperty, s.ScaleX), (1, ScaleTransform.ScaleXProperty, to),
            (0, ScaleTransform.ScaleYProperty, s.ScaleY), (1, ScaleTransform.ScaleYProperty, to),
        });
    }

    /// <summary>WPF Bump: a pill swells 45% and settles when its count goes up.</summary>
    private static void Bump(Border pill)
    {
        double k = FriendsDrawer.Amount;
        if (k <= 0) return;
        if (pill.RenderTransform is not ScaleTransform s) { pill.RenderTransformOrigin = RelativePoint.Center; pill.RenderTransform = s = new ScaleTransform(1, 1); }
        double peak = 1 + 0.45 * k;
        global::ConditioningControlPanel.Avalonia.Helpers.TransformTween.Run(s, TimeSpan.FromMilliseconds(420), new (double, AvaloniaProperty, double)[]
        {
            (0, ScaleTransform.ScaleXProperty, 1), (0.29, ScaleTransform.ScaleXProperty, peak), (0.6, ScaleTransform.ScaleXProperty, 0.94), (1, ScaleTransform.ScaleXProperty, 1),
            (0, ScaleTransform.ScaleYProperty, 1), (0.29, ScaleTransform.ScaleYProperty, peak), (0.6, ScaleTransform.ScaleYProperty, 0.94), (1, ScaleTransform.ScaleYProperty, 1),
        });
    }
    private int _lastCount = -1, _lastUnread = -1;

    private Window? _host;
    private void OnPopupOpened()
    {
        Friends.FriendsSfx.DrawerOpen();
        try
        {
            _host = TopLevel.GetTopLevel(this) as Window;
            (_host as MainShellWindow)?.HoldNavRailOpen(_popup);
            if (_host != null)
            {
                _host.PositionChanged += OnHostMoved;
                _host.PropertyChanged += OnHostState;
                _host.Deactivated += OnHostDeactivated;
            }
            _face.Background = FriendsDrawer.Raised;
            Drawer.OnOpened();
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] drawer open failed: {E}", ex.Message); }
    }
    private void OnPopupClosed()
    {
        Friends.FriendsSfx.DrawerClose();
        try
        {
            if (_host != null)
            {
                _host.PositionChanged -= OnHostMoved;
                _host.PropertyChanged -= OnHostState;
                _host.Deactivated -= OnHostDeactivated;
            }
            (_host as MainShellWindow)?.ReleaseNavRailOpen(_popup);
            _host = null;
            _face.Background = Brushes.Transparent;
            Drawer.OnClosed();
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] drawer close failed: {E}", ex.Message); }
    }
    /// <summary>Test seam: is the host window the active one.</summary>
    internal Func<Window, bool> HostIsActive { get; set; } = w => w.IsActive;
    /// <summary>Test seam: bring the host to the front.</summary>
    internal Action<Window> ActivateHost { get; set; } = w => w.Activate();
    internal int HostActivations { get; private set; }

    /// <summary>A press inside the drawer while another app has the foreground. Without this the code box took
    /// the caret but every key and paste still went to the app in front (Discord, where the code was copied).
    /// Activating the host hands the keyboard back; the pressed text box is focused again once that settled.</summary>
    private void OnDrawerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var host = _host ?? TopLevel.GetTopLevel(this) as Window;
            if (host == null || HostIsActive(host)) return;
            ActivateHost(host);
            HostActivations++;
            TextBox? box = null;
            for (var v = e.Source as Visual; v != null && box == null; v = v.GetVisualParent()) box = v as TextBox;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (box != null) box.Focus(); else Drawer.Focus(); }, global::Avalonia.Threading.DispatcherPriority.Input);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] drawer activate failed: {E}", ex.Message); }
    }
    private void OnHostMoved(object? sender, PixelPointEventArgs e) => _popup.IsOpen = false;
    private void OnHostState(object? sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == Window.WindowStateProperty) _popup.IsOpen = false; }
    /// <summary>Another app came to the front: fold, unless the keyboard is inside the drawer.</summary>
    private void OnHostDeactivated(object? sender, EventArgs e) { if (!Drawer.IsKeyboardFocusWithin) _popup.IsOpen = false; }
    internal bool IsOpen => _popup.IsOpen;

    /// <summary>WPF UpdateTooltip runs on a language switch too: the tooltip is code-set, so rebuild it.</summary>
    private void OnLanguage(object? sender, EventArgs e) => UpdateBadge();

    internal int PillCount => _pill.IsVisible && int.TryParse(_pillText.Text, out var n) ? n : 0;
    internal void Toggle()
    {
        if (_popup.IsOpen) { _popup.IsOpen = false; return; }
        Rebind();
        _popup.IsOpen = true;
    }

    private void Rebind()
    {
        var next = Drawer.Service ?? Platform.FriendsHead.Service;
        if (!ReferenceEquals(next, _svc)) { Unwire(); _svc = next; if (_svc != null) _svc.SnapshotChanged += OnSnapshot; }
        UpdatePill();
        RebindFeed();
    }
    /// <summary>The number the pink unread badge shows, 0 while hidden, 10 for "9+".</summary>
    internal int UnreadBadge => !_badge.IsVisible ? 0 : _badgeText.Text == "9+" ? 10 : int.TryParse(_badgeText.Text, out var n) ? n : 0;
    private void RebindFeed()
    {
        FriendsFeed? next;
        try { next = _resolveFeed(); } catch { next = null; }
        if (!ReferenceEquals(next, _feed)) { UnwireFeed(); _feed = next; if (_feed != null) _feed.Changed += OnFeedChanged; }
        UpdateBadge();
    }
    private void UnwireFeed() { if (_feed != null) _feed.Changed -= OnFeedChanged; _feed = null; }
    private void OnFeedChanged()
    {
        if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { global::Avalonia.Threading.Dispatcher.UIThread.Post(OnFeedChanged); return; }
        UpdateBadge();
    }
    /// <summary>WPF UpdateBadge: the count; the tooltip leads with the name and the new-line count.</summary>
    internal void UpdateBadge()
    {
        int n = 0;
        try { if (_svc?.Available != false) n = _feed?.Unread ?? 0; } catch { }
        _badgeText.Text = FriendsFeedRules.BadgeText(n);
        _badge.IsVisible = n > 0;
        if (n > _lastUnread && _lastUnread >= 0 && n > 0) Bump(_badge);
        _lastUnread = n;
        var who = string.IsNullOrWhiteSpace(_name?.Text) ? "" : _name!.Text + "\n";
        ToolTip.SetTip(this, n > 0 ? who + Loc.Get("friends_chip_tooltip") + "\n" + Loc.GetF("friends_feed_new", n) : who + Loc.Get("friends_chip_tooltip"));
    }
    private void Unwire() { if (_svc != null) _svc.SnapshotChanged -= OnSnapshot; _svc = null; }
    private void OnSnapshot(FriendsSnapshot _) => UpdatePill();
    internal void UpdatePill()
    {
        int n = 0;
        try { if (_svc?.Available == true) n = _svc.Snapshot?.OnlineCount ?? 0; } catch { }
        _pillText.Text = n.ToString();
        _pill.IsVisible = n > 0;
        if (n > _lastCount && _lastCount >= 0 && n > 0) Bump(_pill);
        _lastCount = n;
    }
}
