using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// The run-pick ribbon: a horizontal, click-through line of tiles across the TOP of the
/// screen — one per mantra/sin DRAFTED DURING the descent (the run's <c>RunPickTiles</c>),
/// in pick order. It sits top-RIGHT, next to the right-hand clock (the wave-timer pill), and
/// grows leftward, so a long run's worth of picks all stay visible at a glance without opening
/// the sidebar. Pre-equipped toys/accessories are NOT shown here — only what you take on the way down.
///
/// Same keep-alive contract as the other chaos overlays: created once at run start
/// (<see cref="EnsureCreated"/>), content rebuilt in place (<see cref="SetPicks"/>), the window
/// only closed at run teardown (<see cref="CloseActive"/>) — closing a layered window mid-run can
/// wedge the shared WPF render thread. New keep-alive overlay → it JOINS the z-raise list in
/// ChaosModeService.RaiseGameLayerAboveVideo.
/// </summary>
public sealed class ChaosBoonBarOverlay : Window
{
    private const double TILE = 42;
    private const double CLOCK_RESERVE = 220;   // rightmost strip kept clear for the wave-timer clock pill

    private static ChaosBoonBarOverlay? _active;

    /// <summary>Create the (empty, invisible) window ahead of time at run start.</summary>
    public static void EnsureCreated()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try { if (_active == null) { _active = new ChaosBoonBarOverlay(); ((Window)_active).Show(); } }
                catch (Exception ex) { App.Logger?.Debug("ChaosBoonBar.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Rebuild the ribbon from the current run-pick list. Safe from any thread.</summary>
    public static void SetPicks(IReadOnlyList<ChaosSidebarBoon> picks)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            // Snapshot now (the live ObservableCollection mutates on the UI thread).
            var snapshot = new List<ChaosSidebarBoon>(picks);
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosBoonBarOverlay(); ((Window)_active).Show(); }
                    _active.Rebuild(snapshot);
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosBoonBar.SetPicks: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Hide the ribbon (drafts/pauses) without closing the window.</summary>
    public static void Clear()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() => { try { if (_active != null) _active._pill.Visibility = Visibility.Collapsed; } catch { } });
        }
        catch { }
    }

    /// <summary>Re-stack the live window above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Instant teardown (run end / shutdown).</summary>
    public static void CloseActive()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) { _active = null; return; }
            disp.BeginInvoke(() => { try { _active?.CloseNow(); } catch { } });
        }
        catch { }
    }

    private readonly Border _pill;
    private readonly StackPanel _row;

    private ChaosBoonBarOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        // Hit-testable (NOT click-through) so hovering a tile shows its tooltip. The window is
        // exactly the pill (SizeToContent), tucked in the top-right, and the tiles carry no click
        // handlers — so it captures only harmless hovers over that small strip. WS_EX_NOACTIVATE
        // (ApplyExStyles) keeps it from ever stealing focus from the run.
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;

        _row = new StackPanel { Orientation = Orientation.Horizontal };
        _pill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(150, 0x12, 0x0E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(140, 0xE8, 0x43, 0x93)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 6, 10, 6),
            Visibility = Visibility.Collapsed,
            Child = _row,
        };
        Content = _pill;

        // Top-RIGHT, on the SAME row as the right-hand clock (the wave-timer pill) and just to its
        // LEFT — CLOCK_RESERVE keeps the rightmost strip free for that pill. Right-anchored, so the
        // ribbon grows LEFTWARD (away from the clock) as picks land.
        var wa = SystemParameters.WorkArea;
        Top = wa.Top + 10;
        Left = wa.Right - CLOCK_RESERVE - 200;   // re-aligned precisely once content sizes
        SizeChanged += (_, _) => { try { Left = wa.Right - CLOCK_RESERVE - ActualWidth; } catch { } };

        SourceInitialized += (_, _) => ApplyExStyles();
    }

    private int _shownCount;   // how many tiles were on the ribbon last rebuild — newcomers get the pop-in

    private void Rebuild(List<ChaosSidebarBoon> picks)
    {
        _row.Children.Clear();
        if (picks.Count == 0) { _pill.Visibility = Visibility.Collapsed; _shownCount = 0; return; }
        _pill.Visibility = Visibility.Visible;

        for (int i = 0; i < picks.Count; i++)
        {
            var tile = BuildTile(picks[i]);
            _row.Children.Add(tile);
            if (i >= _shownCount) AnimateTileIn(tile, i - _shownCount);   // only freshly drafted tiles pop in
        }
        _shownCount = picks.Count;
    }

    /// <summary>A new ribbon tile lands: scale-pop + fade in, staggered so a multi-pick rebuild cascades.</summary>
    private static void AnimateTileIn(FrameworkElement tile, int order)
    {
        var st = new ScaleTransform(0.4, 0.4);
        tile.RenderTransformOrigin = new Point(0.5, 0.5);
        tile.RenderTransform = st;
        tile.Opacity = 0;
        var delay = TimeSpan.FromMilliseconds(70 * order);
        var pop = new DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(340))
        { BeginTime = delay, EasingFunction = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        tile.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)) { BeginTime = delay });
    }

    private static FrameworkElement BuildTile(ChaosSidebarBoon b)
    {
        var inner = new Grid
        {
            Clip = new RectangleGeometry(new Rect(0, 0, TILE, TILE), 10, 10),
        };
        if (b.Icon == null)
            inner.Children.Add(new TextBlock
            {
                Text = b.Glyph,
                FontSize = 19,
                Foreground = ChaosBoonColors.SidebarAccent(b),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        else
            inner.Children.Add(new Image { Source = (ImageSource)b.Icon, Stretch = Stretch.UniformToFill });
        inner.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderBrush = ChaosBoonColors.SidebarAccent(b),
            BorderThickness = new Thickness(3),
            IsHitTestVisible = false,
        });

        var tile = new Border
        {
            Width = TILE,
            Height = TILE,
            CornerRadius = new CornerRadius(10),
            Background = ChaosBoonColors.SidebarBack(b),
            Margin = new Thickness(0, 0, 6, 0),
            Child = inner,
        };
        tile.ToolTip = BuildTip(b);
        return tile;
    }

    /// <summary>A non-layered hover card (HasDropShadow=False — the deadlock-safe contract the
    /// HUD's BoonTipChrome uses) naming the drafted pick and what it does.</summary>
    private static ToolTip BuildTip(ChaosSidebarBoon b)
    {
        var panel = new StackPanel { MaxWidth = 240 };
        panel.Children.Add(new TextBlock
        {
            Text = b.Name, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = ChaosBoonColors.SidebarAccent(b),
        });
        if (!string.IsNullOrEmpty(b.Desc))
            panel.Children.Add(new TextBlock
            {
                Text = b.Desc, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xE0, 0xF0)),
            });
        if (!string.IsNullOrEmpty(b.Flavor))
            panel.Children.Add(new TextBlock
            {
                Text = b.Flavor, FontStyle = FontStyles.Italic, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC8)),
            });
        return new ToolTip
        {
            Content = panel,
            HasDropShadow = false,   // non-layered — must stay false (render-thread deadlock history)
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x15, 0x12, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x69, 0xB4)),
            Foreground = Brushes.White,
            Padding = new Thickness(10, 8, 10, 8),
        };
    }

    private void CloseNow()
    {
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            // NO WS_EX_TRANSPARENT — we want hover for tooltips. NOACTIVATE keeps focus off it.
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
