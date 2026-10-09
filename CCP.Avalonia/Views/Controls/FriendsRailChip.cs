// PORTED from ConditioningControlPanel/Controls/Friends/FriendsRailChip.cs: your face and name at the
// foot of the rail with a mint pill for friends online; a click opens the drawer upward over the rail.
// ponytail: no unread badge (the feed is not on this head), no bump / hover lift, no tier plate, no
// rail hold; Popup light-dismiss stands in for WPF's click-outside and host-moved watchers.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed class FriendsRailChip : Grid
{
    private readonly Border _pill = new(), _face = new() { Width = 48, Height = 48, CornerRadius = new CornerRadius(14) };
    private readonly TextBlock _pillText = new(), _name;
    private readonly Popup _popup;
    private IFriendsService? _svc;
    public FriendsRailChip() : this(null) { }
    internal FriendsRailChip(IFriendsService? service)
    {
        Drawer = new FriendsDrawer(service);
        (Height, Margin, Background, Cursor) = (48, new Thickness(0, 2, 0, 4), Brushes.Transparent, FriendsDrawer.Hand());
        ColumnDefinitions = new ColumnDefinitions("56,*");
        (_pillText.FontFamily, _pillText.FontWeight, _pillText.FontSize, _pillText.Foreground) = (FriendsDrawer.Display, FontWeight.SemiBold, 11, FriendsDrawer.MintInk);
        (_pill.Child, _pill.Background, _pill.CornerRadius, _pill.Padding, _pill.IsVisible, _pill.Tag) = (_pillText, FriendsDrawer.Mint, new CornerRadius(999), new Thickness(5, 0), false, "friends-chip-online");
        (_pill.HorizontalAlignment, _pill.VerticalAlignment, _pill.Margin) = (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -2, -4, 0));
        _face.HorizontalAlignment = HorizontalAlignment.Center;
        _face.Child = new Grid { Children = { FriendsDrawer.Avatar(Drawer.MeName(), 40, null), _pill } };
        // The name column is 0 px wide while the rail is shut, so nothing in it may wrap.
        _name = FriendsDrawer.Label(Drawer.MeName(), 14, FriendsDrawer.Text, FriendsDrawer.Display, FontWeight.SemiBold);
        _name.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(_name, 1);
        Children.AddRange(new Control[] { _face, _name });
        ToolTip.SetTip(this, Loc.Get("friends_chip_tooltip"));
        _popup = new Popup
        {
            Child = Drawer, Placement = PlacementMode.Top, PlacementTarget = this, HorizontalOffset = 4, VerticalOffset = -6,
            IsLightDismissEnabled = true,
        };
        Children.Add(_popup);
        _popup.Opened += (_, _) => { _face.Background = FriendsDrawer.Raised; Drawer.OnOpened(); };
        _popup.Closed += (_, _) => { _face.Background = Brushes.Transparent; Drawer.OnClosed(); };
        Drawer.OwnerWindow = () => TopLevel.GetTopLevel(this) as Window;
        Drawer.CloseRequested += () => _popup.IsOpen = false;
        Drawer.SettingsRequested += () => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("appsettings");
        Drawer.SignInRequested += () => { _popup.IsOpen = false; _ = (TopLevel.GetTopLevel(this) as MainShellWindow)?.OpenUnifiedLoginDialog(); };
        Drawer.InvitesRequested += () => { _popup.IsOpen = false; (TopLevel.GetTopLevel(this) as MainShellWindow)?.OpenInvitesCard(); };
        PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) { Toggle(); e.Handled = true; } };
        AttachedToVisualTree += (_, _) => Rebind();
        DetachedFromVisualTree += (_, _) => { _popup.IsOpen = false; Unwire(); Drawer.Unsubscribe(); };
        Rebind();
    }
    internal FriendsDrawer Drawer { get; }
    internal bool IsOpen => _popup.IsOpen;

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
    }
    private void Unwire() { if (_svc != null) _svc.SnapshotChanged -= OnSnapshot; _svc = null; }
    private void OnSnapshot(FriendsSnapshot _) => UpdatePill();
    internal void UpdatePill()
    {
        int n = 0;
        try { if (_svc?.Available == true) n = _svc.Snapshot?.OnlineCount ?? 0; } catch { }
        _pillText.Text = n.ToString();
        _pill.IsVisible = n > 0;
    }
}
