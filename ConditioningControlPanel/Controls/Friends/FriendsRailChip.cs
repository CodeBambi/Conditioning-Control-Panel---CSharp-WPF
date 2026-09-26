using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Launcher;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// THE "YOU" CHIP: bottom-left, your face, your name, your tier plate and a mint pill with how
/// many friends are online. Click it and the friends drawer opens upward over the rail.
///
/// <para><b>Self-wiring, like <c>EmiDock</c> and <c>DescentFuseRailChip</c>.</b> Adding the
/// element is the whole integration: no init call, no startup line. The same control sits in
/// the panel's rail and in the launcher's bottom row, each with its own drawer.</para>
///
/// <para><b>Not a door.</b> No NavDoorMap row, no "you are here" ring, and none of the door
/// Viewbox markup NavRailFlyoutTests counts in MainWindow.xaml: it lives in its own file.</para>
///
/// <para><b>The rail hold.</b> While the drawer is up in the panel the chip holds the nav rail
/// open, keyed on its own popup, and lets go exactly once when the popup closes. The rail's
/// watchdog may still fold the rail under an open drawer when the pointer sits on the drawer
/// past the rail's edge; the drawer is its own window and stays put, and the late release is a
/// no-op on a latch that was already cleared.</para>
/// </summary>
public sealed class FriendsRailChip : UserControl
{
    private readonly Func<IFriendsService?> _resolve;
    private IFriendsService? _svc;
    private bool _wired;

    private readonly Grid _avatarHost = new() { Width = 40, Height = 40 };
    private readonly Border _pill = new();
    private readonly TextBlock _pillText = new();
    private readonly TextBlock _name = new();
    private readonly StackPanel _nameLine = new() { Orientation = Orientation.Horizontal };
    private readonly Border _face = new();
    private readonly Popup _popup;
    private readonly FriendsDrawer _drawer;

    private Window? _host;
    private DateTime _closedAt = DateTime.MinValue;
    private int _lastCount = -1;

    /// <summary>The chip. <paramref name="service"/> is for the suite; the app passes nothing
    /// and the chip reads <c>App.Friends</c>.</summary>
    public FriendsRailChip() : this(null) { }

    internal FriendsRailChip(IFriendsService? service)
    {
        _resolve = service != null ? () => service : () => App.Friends;
        Margin = new Thickness(0, 2, 0, 4);
        Focusable = false;

        var grid = new Grid { Height = 48, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _face.Width = 48;
        _face.Height = 48;
        _face.CornerRadius = new CornerRadius(14);
        _face.BorderThickness = new Thickness(1);
        _face.BorderBrush = Brushes.Transparent;
        _face.Background = Brushes.Transparent;
        _face.HorizontalAlignment = HorizontalAlignment.Center;
        _face.RenderTransformOrigin = new Point(0.5, 0.5);
        _face.RenderTransform = new ScaleTransform(1, 1);

        var faceGrid = new Grid();
        _avatarHost.HorizontalAlignment = HorizontalAlignment.Center;
        _avatarHost.VerticalAlignment = VerticalAlignment.Center;
        faceGrid.Children.Add(_avatarHost);

        _pillText.FontFamily = FriendsLook.Display;
        _pillText.FontWeight = FontWeights.SemiBold;
        _pillText.FontSize = 11;
        _pillText.Foreground = FriendsLook.MintInkBrush;
        _pill.Child = _pillText;
        _pill.Background = FriendsLook.MintBrush;
        _pill.CornerRadius = new CornerRadius(999);
        _pill.Padding = new Thickness(5, 0, 5, 0);
        _pill.BorderThickness = new Thickness(2);
        _pill.BorderBrush = FriendsLook.Frozen(FriendsLook.Rgb(0x0E, 0x09, 0x19));
        _pill.HorizontalAlignment = HorizontalAlignment.Right;
        _pill.VerticalAlignment = VerticalAlignment.Top;
        _pill.Margin = new Thickness(0, -2, -4, 0);
        _pill.Visibility = Visibility.Collapsed;
        _pill.RenderTransformOrigin = new Point(0.5, 0.5);
        _pill.RenderTransform = new ScaleTransform(1, 1);
        _pill.Effect = FriendsLook.Glow(FriendsLook.Mint, 8, 0.7);
        faceGrid.Children.Add(_pill);
        _face.Child = faceGrid;
        grid.Children.Add(_face);

        // The name column is 0 px wide while the rail is shut, so nothing in it may wrap: a
        // wrapping TextBlock measured at width 0 stands one character per line (EmiDock's trap).
        _name.FontFamily = FriendsLook.Display;
        _name.FontWeight = FontWeights.SemiBold;
        _name.FontSize = 14;
        _name.Foreground = FriendsLook.TextBrush;
        _name.TextWrapping = TextWrapping.NoWrap;
        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        _name.VerticalAlignment = VerticalAlignment.Center;
        _nameLine.Children.Add(_name);
        _nameLine.VerticalAlignment = VerticalAlignment.Center;
        _nameLine.Margin = new Thickness(0, 0, 10, 0);
        _nameLine.ClipToBounds = true;
        Grid.SetColumn(_nameLine, 1);
        grid.Children.Add(_nameLine);

        Content = grid;
        ToolTip = Loc.Get("friends_chip_tooltip");

        _drawer = new FriendsDrawer(service);
        _drawer.CloseRequested += () => _popup!.IsOpen = false;
        _drawer.SettingsRequested += OpenSettings;
        _popup = new Popup
        {
            Child = _drawer,
            AllowsTransparency = true,
            StaysOpen = true,
            Placement = PlacementMode.Top,
            PlacementTarget = this,
            HorizontalOffset = 4,
            VerticalOffset = -6,
            PopupAnimation = PopupAnimation.None,
            Focusable = true,
        };
        _popup.Opened += OnPopupOpened;
        _popup.Closed += OnPopupClosed;
        _drawer.PreviewMouseDown += OnDrawerMouseDown;

        grid.MouseLeftButtonUp += (_, e) => { Toggle(); e.Handled = true; };
        grid.MouseEnter += (_, _) => Hover(true);
        grid.MouseLeave += (_, _) => Hover(false);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RefreshFace();
    }

    /// <summary>The drawer this chip opens. The suite reads it.</summary>
    internal FriendsDrawer Drawer => _drawer;

    internal bool IsOpen => _popup.IsOpen;

    /// <summary>The number the mint pill shows, or 0 while it is hidden.</summary>
    internal int PillCount => _pill.Visibility == Visibility.Visible && int.TryParse(_pillText.Text, out var n) ? n : 0;

    // ---- lifecycle --------------------------------------------------------------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Rebind();
            _host = Window.GetWindow(this);
            if (_host != null) _host.Activated += OnHostActivated;
            RefreshFace();
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] chip load failed: {E}", ex.Message); }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _popup.IsOpen = false;
            if (_host != null) _host.Activated -= OnHostActivated;
            Unwire();
            _drawer.Unsubscribe();
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] chip unload failed: {E}", ex.Message); }
    }

    /// <summary>App.Friends is rebuilt on sign in and sign out; the window coming to the front is
    /// a cheap moment to notice a new one.</summary>
    private void OnHostActivated(object? sender, EventArgs e)
    {
        Rebind();
        RefreshFace();
        if (_popup.IsOpen) DropTopmost();
    }

    /// <summary>A WPF popup is a TOPMOST window, so a drawer left open floated over every other
    /// app (a player's browser, bug report 6.11). The popup is owned by the host window, so
    /// without topmost it still sits above the panel or launcher, and goes behind other apps with
    /// them. Re-applied on open and on every activation, in case WPF restores the flag.</summary>
    private void DropTopmost()
    {
        try
        {
            if (PresentationSource.FromVisual(_drawer) is HwndSource src && src.Handle != IntPtr.Zero)
                SetWindowPos(src.Handle, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] drawer topmost drop failed: {E}", ex.Message); }
    }

    private static readonly IntPtr HwndNoTopmost = new(-2);
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    internal void Rebind()
    {
        var next = _resolve();
        if (ReferenceEquals(next, _svc) && _wired) return;
        Unwire();
        _svc = next;
        if (_svc != null)
        {
            _svc.SnapshotChanged += OnSnapshot;
            _wired = true;
        }
        UpdatePill(animate: false);
    }

    private void Unwire()
    {
        if (_svc != null && _wired) _svc.SnapshotChanged -= OnSnapshot;
        _wired = false;
    }

    private void OnSnapshot(FriendsSnapshot _)
    {
        UpdatePill(animate: true);
    }

    // ---- painting ---------------------------------------------------------------------

    private void RefreshFace()
    {
        try
        {
            var name = _drawer.MeName();
            _name.Text = name;
            _avatarHost.Children.Clear();
            _avatarHost.Children.Add(FriendsLook.Avatar(name, null, 40));

            while (_nameLine.Children.Count > 1) _nameLine.Children.RemoveAt(1);
            if (FriendsLook.TierPlate(_drawer.MeTier(), 13) is { } plate) _nameLine.Children.Add(plate);
            UpdatePill(animate: false);
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] chip paint failed: {E}", ex.Message); }
    }

    internal void UpdatePill(bool animate)
    {
        int n = 0;
        try { if (_svc?.Available == true) n = _svc.Snapshot?.OnlineCount ?? 0; } catch { }
        _pillText.Text = n.ToString();
        _pill.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (animate && n > _lastCount && _lastCount >= 0 && n > 0) Bump();
        _lastCount = n;
    }

    /// <summary>The pill bumps when the count goes up.</summary>
    private void Bump()
    {
        double k = FriendsDrawer.Amount;
        if (k <= 0 || _pill.RenderTransform is not ScaleTransform s) return;
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1 + 0.45 * k, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)),
            new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut }));
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private void Hover(bool on)
    {
        _face.Background = on || _popup.IsOpen ? FriendsLook.RaisedBrush : Brushes.Transparent;
        MotionFx.HoverLift(_face, on);
    }

    // ---- open / close -----------------------------------------------------------------

    /// <summary>Opens or folds the drawer. A click on the chip that itself folded the drawer
    /// (the click-outside watcher fires on the same press) must not open it again.</summary>
    internal void Toggle()
    {
        if (_popup.IsOpen) { _popup.IsOpen = false; return; }
        if ((DateTime.UtcNow - _closedAt).TotalMilliseconds < 250) return;
        Rebind();
        _popup.IsOpen = true;
    }

    /// <summary>Opens the drawer if it is folded (an Inbox row asking for it).</summary>
    internal void OpenDrawer()
    {
        if (_popup.IsOpen) return;
        Rebind();
        _popup.IsOpen = true;
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        Services.Friends.FriendsSfx.DrawerOpen();
        try
        {
            if (_host is MainWindow mw) mw.HoldNavRailOpen(_popup);
            if (_host != null)
            {
                _host.PreviewMouseDown += OnHostMouseDown;
                _host.LocationChanged += OnHostMoved;
                _host.StateChanged += OnHostMoved;
                _host.Deactivated += OnHostDeactivated;
            }
            _face.Background = FriendsLook.RaisedBrush;
            _face.BorderBrush = FriendsLook.Line2Brush;
            DropTopmost();
            _drawer.OnOpened();
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] drawer open failed: {E}", ex.Message); }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        Services.Friends.FriendsSfx.DrawerClose();
        try
        {
            _closedAt = DateTime.UtcNow;
            if (_host != null)
            {
                _host.PreviewMouseDown -= OnHostMouseDown;
                _host.LocationChanged -= OnHostMoved;
                _host.StateChanged -= OnHostMoved;
                _host.Deactivated -= OnHostDeactivated;
            }
            if (_host is MainWindow mw) mw.ReleaseNavRailOpen(_popup);
            _face.Background = Brushes.Transparent;
            _face.BorderBrush = Brushes.Transparent;
            _drawer.OnClosed();
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] drawer close failed: {E}", ex.Message); }
    }

    /// <summary>Any press in the host window that is not on the chip folds the drawer. The
    /// drawer is its own window, so a press inside it never reaches here.</summary>
    private void OnHostMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsInside(d)) return;
        _popup.IsOpen = false;
    }

    private bool IsInside(DependencyObject d)
    {
        var cur = d;
        while (cur != null)
        {
            if (ReferenceEquals(cur, this)) return true;
            cur = cur is Visual || cur is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(cur)
                : LogicalTreeHelper.GetParent(cur);
        }
        return false;
    }

    /// <summary>A press inside the drawer while another app has the foreground. A popup never
    /// activates its owner on a click, so without this the code box took the caret but every
    /// key and every paste still went to the app in front (Discord, where the code was copied).
    /// Activating the host hands the keyboard back; the pressed text box is focused again once
    /// the activation has settled.</summary>
    private void OnDrawerMouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_host == null || _host.IsActive) return;
            _host.Activate();
            var box = FindTextBox(e.OriginalSource as DependencyObject);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (box != null) { box.Focus(); Keyboard.Focus(box); }
                else _drawer.Focus();
            }));
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] drawer activate failed: {E}", ex.Message); }
    }

    private static TextBox? FindTextBox(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is TextBox t) return t;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private void OnHostMoved(object? sender, EventArgs e) => _popup.IsOpen = false;

    /// <summary>Another app came to the front. A context menu inside the drawer does not
    /// deactivate the host, so this only fires when the player really left.</summary>
    private void OnHostDeactivated(object? sender, EventArgs e)
    {
        if (_drawer.IsKeyboardFocusWithin) return;
        _popup.IsOpen = false;
    }

    private void OpenSettings()
    {
        try
        {
            if (_host is MainWindow mw) mw.ShowTab("appsettings");
            else LauncherHost.OpenPanelTab("appsettings");
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] settings failed: {E}", ex.Message); }
    }
}
